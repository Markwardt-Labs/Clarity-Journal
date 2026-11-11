namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class EntryTitlerTests
{
    private readonly Mock<IEntryStore> entries = new();

    private readonly Mock<IClaudeRunner> claude = new();

    private readonly Mock<IJournalExport> export = new();

    private readonly EventHub events = new();

    private readonly List<RemoteEvent> published = [];

    private readonly List<ClaudeRequest> requests = [];

    private readonly Prompts prompts = new();

    private readonly ServerConfig config = new() { Key = "k" };

    private readonly EntryTitler titler;

    public EntryTitlerTests()
    {
        events.Subscribe(published.Add);
        export.SetupGet(target => target.Folder).Returns("/export");
        entries.Setup(store => store.SetDigest(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        entries.Setup(store => store.GetDigest(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync((DateOnly date, CancellationToken _) => new EntryDigest { Date = date, Title = "Stored", Summary = "Stored.", Excerpt = "x" });
        claude.Setup(runner => runner.Run(config, It.IsAny<ClaudeRequest>(), It.IsAny<CancellationToken>())).Callback<ServerConfig, ClaudeRequest, CancellationToken>((_, request, _) => requests.Add(request)).ReturnsAsync("{\"title\": \"Pasta night\", \"summary\": \"You cooked for friends.\"}");
        titler = new EntryTitler(entries.Object, claude.Object, prompts, export.Object, events, new FixedTimeProvider(new DateTimeOffset(2026, 10, 16, 0, 5, 0, TimeSpan.Zero)), Mock.Of<ILog>());
    }

    private JournalEntry Entry(string date, string text = "Cooked pasta for friends")
        => new() { Date = DateOnly.Parse(date, CultureInfo.InvariantCulture), Text = text, UpdatedAt = DateTimeOffset.UnixEpoch };

    private void Pending(params JournalEntry[] pending)
        => entries.Setup(store => store.ListUntitled(new DateOnly(2026, 10, 16), It.IsAny<CancellationToken>())).ReturnsAsync(pending);

    [Fact]
    public async Task Run_TitlesEveryPendingEntryFromItsOwnTextAndAnnouncesTheDigest()
    {
        Pending(Entry("2026-10-15", "Cooked pasta"), Entry("2026-10-14", "Went running"));

        Assert.True(await titler.Run(config, CancellationToken.None));

        entries.Verify(store => store.SetDigest(new DateOnly(2026, 10, 15), "Cooked pasta", "Pasta night", "You cooked for friends.", It.IsAny<CancellationToken>()), Times.Once);
        entries.Verify(store => store.SetDigest(new DateOnly(2026, 10, 14), "Went running", "Pasta night", "You cooked for friends.", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal([new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 14)], published.Select(remoteEvent => remoteEvent.Date!.Value));
        Assert.All(published, remoteEvent => Assert.Equal(RemoteEventKind.EntryChanged, remoteEvent.Kind));
        Assert.All(published, remoteEvent => Assert.Equal("Stored", remoteEvent.Digest!.Title));
    }

    [Fact]
    public async Task Run_AsksClaudeWithoutToolsAboutOneEntryAtATime()
    {
        Pending(Entry("2026-10-15", "Cooked pasta"), Entry("2026-10-14", "Went running"));

        await titler.Run(config, CancellationToken.None);

        Assert.Equal(2, requests.Count);
        Assert.All(requests, request =>
        {
            Assert.Empty(request.Tools);
            Assert.True(request.ReplacesSystemPrompt);
            Assert.Equal(prompts.TitleSystem, request.SystemPrompt);
            Assert.Equal("/export", request.Folder);
            Assert.Null(request.Session);
        });
        Assert.Contains("2026-10-15", requests[0].Prompt);
        Assert.Contains("Cooked pasta", requests[0].Prompt);
        Assert.DoesNotContain("Went running", requests[0].Prompt);
    }

    [Fact]
    public async Task Run_OnlyAsksForDaysBeforeTheServersToday()
    {
        Pending();

        await titler.Run(config, CancellationToken.None);

        entries.Verify(store => store.ListUntitled(new DateOnly(2026, 10, 16), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_NothingPending_AsksClaudeNothing()
    {
        Pending();

        Assert.True(await titler.Run(config, CancellationToken.None));

        claude.Verify(runner => runner.Run(It.IsAny<ServerConfig>(), It.IsAny<ClaudeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(published);
    }

    [Fact]
    public async Task Run_ClaudeFails_StopsWithoutStoringAnythingAndReportsIt()
    {
        Pending(Entry("2026-10-15"), Entry("2026-10-14"));
        claude.Setup(runner => runner.Run(config, It.IsAny<ClaudeRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ClaudeException("not logged in"));

        Assert.False(await titler.Run(config, CancellationToken.None));

        claude.Verify(runner => runner.Run(It.IsAny<ServerConfig>(), It.IsAny<ClaudeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        entries.Verify(store => store.SetDigest(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(published);
    }

    [Fact]
    public async Task Run_StorageFails_ReportsItInsteadOfThrowing()
    {
        entries.Setup(store => store.ListUntitled(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ThrowsAsync(new SqliteException("disk full", 13));

        Assert.False(await titler.Run(config, CancellationToken.None));
    }

    [Fact]
    public async Task Run_AnAnswerThatIsNotATitleSkipsThatEntryAndCarriesOn()
    {
        Pending(Entry("2026-10-15", "First"), Entry("2026-10-14", "Second"));
        Queue<string> replies = new(["I am sorry, I cannot do that.", "{\"title\": \"Second day\", \"summary\": \"You wrote again.\"}"]);
        claude.Setup(runner => runner.Run(config, It.IsAny<ClaudeRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => replies.Dequeue());

        Assert.True(await titler.Run(config, CancellationToken.None));

        entries.Verify(store => store.SetDigest(new DateOnly(2026, 10, 15), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        entries.Verify(store => store.SetDigest(new DateOnly(2026, 10, 14), "Second", "Second day", "You wrote again.", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(new DateOnly(2026, 10, 14), Assert.Single(published).Date);
    }

    [Fact]
    public async Task Run_AnEntryEditedWhileItsTitleWasBeingMadeIsNotAnnounced()
    {
        Pending(Entry("2026-10-15"));
        entries.Setup(store => store.SetDigest(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Assert.True(await titler.Run(config, CancellationToken.None));

        Assert.Empty(published);
    }

    [Theory]
    [InlineData("{\"title\": \"Pasta night\", \"summary\": \"You cooked.\"}", "Pasta night", "You cooked.")]
    [InlineData("```json\n{\"title\": \"Pasta night\", \"summary\": \"You cooked.\"}\n```", "Pasta night", "You cooked.")]
    [InlineData("Here you go:\n{\"title\": \"Pasta night\", \"summary\": \"You cooked.\"}\nHope that helps!", "Pasta night", "You cooked.")]
    [InlineData("{\"title\": \"\\\"Pasta night.\\\"\", \"summary\": \"  You   cooked\\nfor friends.  \"}", "Pasta night", "You cooked for friends.")]
    [InlineData("{\"title\": \"“Pasta night”\", \"summary\": \"You cooked.\"}", "Pasta night", "You cooked.")]
    public async Task Run_ReadsTheTitleAndSummaryOutOfTheAnswerAndTidiesThem(string reply, string title, string summary)
    {
        Pending(Entry("2026-10-15"));
        claude.Setup(runner => runner.Run(config, It.IsAny<ClaudeRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(reply);

        await titler.Run(config, CancellationToken.None);

        entries.Verify(store => store.SetDigest(It.IsAny<DateOnly>(), It.IsAny<string>(), title, summary, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no braces at all")]
    [InlineData("{ not json }")]
    [InlineData("[\"title\", \"summary\"]")]
    [InlineData("{\"title\": \"Only a title\"}")]
    [InlineData("{\"summary\": \"Only a summary.\"}")]
    [InlineData("{\"title\": 5, \"summary\": \"You cooked.\"}")]
    [InlineData("{\"title\": \"Pasta night\", \"summary\": null}")]
    [InlineData("{\"title\": \"   \", \"summary\": \"You cooked.\"}")]
    [InlineData("{\"title\": \"Pasta night\", \"summary\": \"\"}")]
    public async Task Run_RefusesAnAnswerThatLacksAUsableTitleAndSummary(string reply)
    {
        Pending(Entry("2026-10-15"));
        claude.Setup(runner => runner.Run(config, It.IsAny<ClaudeRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(reply);

        Assert.True(await titler.Run(config, CancellationToken.None));

        entries.Verify(store => store.SetDigest(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(published);
    }

    [Fact]
    public async Task Run_KeepsATitleAndSummaryShortEvenWhenTheAnswerIsLong()
    {
        Pending(Entry("2026-10-15"));
        claude.Setup(runner => runner.Run(config, It.IsAny<ClaudeRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync($"{{\"title\": \"{string.Join(' ', Enumerable.Repeat("long", 60))}\", \"summary\": \"{string.Join(' ', Enumerable.Repeat("words", 200))}\"}}");
        string? title = null;
        string? summary = null;
        entries.Setup(store => store.SetDigest(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Callback<DateOnly, string, string, string, CancellationToken>((_, _, newTitle, newSummary, _) => (title, summary) = (newTitle, newSummary)).ReturnsAsync(true);

        await titler.Run(config, CancellationToken.None);

        Assert.InRange(title!.Length, 20, 83);
        Assert.EndsWith("...", title, StringComparison.Ordinal);
        Assert.InRange(summary!.Length, 100, 303);
        Assert.EndsWith("...", summary, StringComparison.Ordinal);
    }
}
