namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class SummarizerTests
{
    private readonly Mock<IEntryStore> entries = new();

    private readonly Mock<ISummaryStore> summaries = new();

    private readonly Mock<IClaudeRunner> claude = new();

    private readonly Mock<IJournalExport> export = new();

    private readonly EventHub events = new();

    private readonly List<RemoteEvent> published = [];

    private readonly List<ClaudeRequest> requests = [];

    private readonly List<StoredSummary> saved = [];

    private readonly ServerConfig config = new() { Key = "k", SummaryBatchCharacters = 1000 };

    private readonly Summarizer summarizer;

    public SummarizerTests()
    {
        events.Subscribe(published.Add);
        export.SetupGet(target => target.Folder).Returns("/export");
        entries.Setup(store => store.Count(It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        summaries.Setup(store => store.Get(It.IsAny<SummaryKind>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((StoredSummary?)null);
        summaries.Setup(store => store.Save(It.IsAny<StoredSummary>(), It.IsAny<CancellationToken>())).Callback<StoredSummary, CancellationToken>((summary, _) => saved.Add(summary)).Returns(Task.CompletedTask);
        claude.Setup(runner => runner.Run(config, It.IsAny<ClaudeRequest>(), It.IsAny<CancellationToken>())).Callback<ServerConfig, ClaudeRequest, CancellationToken>((_, request, _) => requests.Add(request)).ReturnsAsync(() => $"summary {requests.Count}");
        summarizer = new Summarizer(entries.Object, summaries.Object, claude.Object, new Prompts(), export.Object, events, new FixedTimeProvider(new DateTimeOffset(2026, 10, 16, 20, 0, 0, TimeSpan.Zero)), Mock.Of<ILog>());
    }

    private RevisedEntry Revised(string date, long revision, string text = "text") => new() { Entry = new JournalEntry { Date = DateOnly.Parse(date, CultureInfo.InvariantCulture), Text = text, UpdatedAt = DateTimeOffset.UnixEpoch }, Revision = revision };

    private void Pending(long taken, long latest, params RevisedEntry[] pending)
    {
        summaries.Setup(store => store.GetSummarizedRevision(It.IsAny<CancellationToken>())).ReturnsAsync(taken);
        entries.Setup(store => store.LatestRevision(It.IsAny<CancellationToken>())).ReturnsAsync(latest);
        entries.Setup(store => store.ListSince(taken, latest, It.IsAny<CancellationToken>())).ReturnsAsync(pending);
    }

    [Fact]
    public async Task Run_FirstRun_GroupsEntriesByTheirPeriod()
    {
        Pending(0, 3, Revised("2026-10-16", 1), Revised("2026-10-19", 2), Revised("2026-11-02", 3));

        await summarizer.Run(config, CancellationToken.None);

        Assert.Equal(["Week:2026-10-12", "Week:2026-10-19", "Week:2026-11-02", "Month:2026-10", "Month:2026-11", "Year:2026", "Lifetime:lifetime"], saved.Select(summary => $"{summary.Summary.Kind}:{summary.Summary.Period}"));
        Assert.Equal(2, saved.Single(summary => summary.Summary.Kind == SummaryKind.Month && summary.Summary.Period == "2026-10").ThroughRevision);
        Assert.Equal(3, saved.Single(summary => summary.Summary.Kind == SummaryKind.Lifetime).ThroughRevision);
        summaries.Verify(store => store.SetSummarizedRevision(3, It.IsAny<CancellationToken>()), Times.Once);
        summaries.Verify(store => store.SetLastRun(new DateTimeOffset(2026, 10, 16, 20, 0, 0, TimeSpan.Zero), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_WithAPreviousSummary_GivesClaudeItAndOnlyTheNewEntries()
    {
        Pending(1, 2, Revised("2026-10-19", 2, "Ran again."));
        StoredSummary month = new() { Summary = new Summary { Kind = SummaryKind.Month, Period = "2026-10", Start = new DateOnly(2026, 10, 1), End = new DateOnly(2026, 10, 31), Text = "You started running.", EntryCount = 1, UpdatedAt = DateTimeOffset.UnixEpoch }, ThroughRevision = 1 };
        summaries.Setup(store => store.Get(SummaryKind.Month, "2026-10", It.IsAny<CancellationToken>())).ReturnsAsync(month);

        await summarizer.Run(config, CancellationToken.None);

        ClaudeRequest request = requests.Single(candidate => candidate.Prompt.Contains("the month 2026-10", StringComparison.Ordinal));
        Assert.Contains("You started running.", request.Prompt);
        Assert.Contains("Ran again.", request.Prompt);
        Assert.Equal("/export", request.Folder);
        Assert.True(request.ReplacesSystemPrompt);
        Assert.Empty(request.Tools);
        Assert.Null(request.Session);
        Assert.Equal(2, saved.Single(summary => summary.Summary.Kind == SummaryKind.Month).ThroughRevision);
    }

    [Fact]
    public async Task Run_PeriodAlreadyCoveredByItsOwnSummary_IsNotSummarizedAgain()
    {
        Pending(0, 2, Revised("2026-10-16", 1), Revised("2026-10-19", 2));
        StoredSummary week = new() { Summary = new Summary { Kind = SummaryKind.Week, Period = "2026-10-12", Start = new DateOnly(2026, 10, 12), End = new DateOnly(2026, 10, 18), Text = "done", EntryCount = 1, UpdatedAt = DateTimeOffset.UnixEpoch }, ThroughRevision = 1 };
        summaries.Setup(store => store.Get(SummaryKind.Week, "2026-10-12", It.IsAny<CancellationToken>())).ReturnsAsync(week);

        await summarizer.Run(config, CancellationToken.None);

        Assert.DoesNotContain(saved, summary => summary.Summary is { Kind: SummaryKind.Week, Period: "2026-10-12" });
        Assert.Contains(saved, summary => summary.Summary is { Kind: SummaryKind.Week, Period: "2026-10-19" });
    }

    [Fact]
    public async Task Run_NothingNew_AsksClaudeNothingButStillRecordsTheRun()
    {
        Pending(5, 5);

        Assert.True(await summarizer.Run(config, CancellationToken.None));

        claude.Verify(runner => runner.Run(It.IsAny<ServerConfig>(), It.IsAny<ClaudeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        summaries.Verify(store => store.SetLastRun(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        summaries.Verify(store => store.Save(It.IsAny<StoredSummary>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(published);
    }

    [Fact]
    public async Task Run_ManyEntriesInOnePeriod_FoldsThemInBatchesOrderedByRevision()
    {
        Pending(0, 3, Revised("2026-10-14", 3, new string('c', 600)), Revised("2026-10-12", 1, new string('a', 600)), Revised("2026-10-13", 2, new string('b', 600)));

        await summarizer.Run(config, CancellationToken.None);

        List<ClaudeRequest> weekRuns = [.. requests.Where(request => request.Prompt.Contains("the week of", StringComparison.Ordinal))];
        Assert.Equal(3, weekRuns.Count);
        Assert.Contains("aaaa", weekRuns[0].Prompt);
        Assert.Contains("bbbb", weekRuns[1].Prompt);
        Assert.Contains("summary 1", weekRuns[1].Prompt);
        Assert.Contains("cccc", weekRuns[2].Prompt);
        Assert.Equal([1L, 2L, 3L], saved.Where(summary => summary.Summary.Kind == SummaryKind.Week).Select(summary => summary.ThroughRevision));
    }

    [Fact]
    public async Task Run_LifetimeSummary_SpansTheFirstToTheLastEntryDay()
    {
        Pending(0, 2, Revised("2026-03-05", 1), Revised("2026-10-16", 2));

        await summarizer.Run(config, CancellationToken.None);

        Summary lifetime = saved.Single(summary => summary.Summary.Kind == SummaryKind.Lifetime).Summary;
        Assert.Equal(new DateOnly(2026, 3, 5), lifetime.Start);
        Assert.Equal(new DateOnly(2026, 10, 16), lifetime.End);
    }

    [Fact]
    public async Task Run_WritesEachSummaryToTheExportAndAnnouncesIt()
    {
        Pending(0, 1, Revised("2026-10-16", 1));

        await summarizer.Run(config, CancellationToken.None);

        export.Verify(target => target.WriteSummary(It.IsAny<Summary>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
        Assert.Equal(4, published.Count(remoteEvent => remoteEvent.Kind == RemoteEventKind.SummariesChanged));
    }

    [Fact]
    public async Task Run_ClaudeFails_ReportsItWithoutAdvancingProgress()
    {
        Pending(0, 1, Revised("2026-10-16", 1));
        claude.Setup(runner => runner.Run(It.IsAny<ServerConfig>(), It.IsAny<ClaudeRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ClaudeException("no network"));

        Assert.False(await summarizer.Run(config, CancellationToken.None));

        summaries.Verify(store => store.SetSummarizedRevision(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        summaries.Verify(store => store.SetLastRun(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains(published, remoteEvent => remoteEvent.Kind == RemoteEventKind.Failed && remoteEvent.Error!.Contains("no network", StringComparison.Ordinal));
    }
}
