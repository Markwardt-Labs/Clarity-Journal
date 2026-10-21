namespace Markwardt.ClarityJournal.Tests.Integration;

public sealed class StoreTests : IAsyncLifetime
{
    private readonly TempFolder temp = new();

    private readonly Database database = new();

    private readonly DateTimeOffset at = new(2026, 10, 16, 21, 0, 0, TimeSpan.FromHours(-5));

    private EntryStore entries = null!;

    private SummaryStore summaries = null!;

    private ChatStore chats = null!;

    private MetaStore meta = null!;

    public async Task InitializeAsync()
    {
        await database.Initialize(temp.Path, CancellationToken.None);
        meta = new MetaStore(database);
        entries = new EntryStore(database);
        summaries = new SummaryStore(database, meta);
        chats = new ChatStore(database);
    }

    public Task DisposeAsync()
    {
        temp.Dispose();
        return Task.CompletedTask;
    }

    private DateOnly Day(string date) => DateOnly.Parse(date, CultureInfo.InvariantCulture);

    [Fact]
    public async Task Initialize_CanRunAgainOnAnExistingDatabaseWithoutLosingData()
    {
        await entries.Save(Day("2026-10-16"), "kept", at, CancellationToken.None);

        await new Database().Initialize(temp.Path, CancellationToken.None);

        Assert.Equal("kept", (await entries.Get(Day("2026-10-16"), CancellationToken.None))!.Text);
    }

    [Fact]
    public async Task Open_BeforeInitialize_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new Database().Open(CancellationToken.None));
    }

    [Fact]
    public async Task Entries_ADayHasExactlyOneEntryWhichSavingReplaces()
    {
        await entries.Save(Day("2026-10-16"), "first", at, CancellationToken.None);
        JournalEntry second = await entries.Save(Day("2026-10-16"), "second", at.AddHours(1), CancellationToken.None);

        Assert.Equal("second", second.Text);
        JournalEntry stored = Assert.Single(await entries.List(null, null, CancellationToken.None));
        Assert.Equal("second", stored.Text);
        Assert.Equal(at.AddHours(1), stored.UpdatedAt);
        Assert.Equal(1, await entries.Count(null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Entries_GetReturnsNothingForADayWithoutOne()
    {
        Assert.Null(await entries.Get(Day("2026-10-16"), CancellationToken.None));
    }

    [Fact]
    public async Task Entries_ListAndCountRespectBothBoundsInclusiveAndOrderByDate()
    {
        foreach (string date in new[] { "2026-10-20", "2026-10-01", "2026-10-31", "2026-11-01", "2026-09-30" })
        {
            await entries.Save(Day(date), date, at, CancellationToken.None);
        }

        Assert.Equal(["2026-10-01", "2026-10-20", "2026-10-31"], (await entries.List(Day("2026-10-01"), Day("2026-10-31"), CancellationToken.None)).Select(entry => entry.Text));
        Assert.Equal(3, await entries.Count(Day("2026-10-01"), Day("2026-10-31"), CancellationToken.None));
        Assert.Equal(["2026-10-31", "2026-11-01"], (await entries.List(Day("2026-10-31"), null, CancellationToken.None)).Select(entry => entry.Text));
        Assert.Equal(["2026-09-30", "2026-10-01"], (await entries.List(null, Day("2026-10-01"), CancellationToken.None)).Select(entry => entry.Text));
        Assert.Equal(5, (await entries.List(null, null, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Entries_KeepTheirTextExactlyIncludingUnicodeAndNewlines()
    {
        string text = "Line one\n\nLine \"two\" with 'quotes', emoji \U0001F600 and ümlauts; DROP TABLE entries;--";
        await entries.Save(Day("2026-10-16"), text, at, CancellationToken.None);

        Assert.Equal(text, (await entries.Get(Day("2026-10-16"), CancellationToken.None))!.Text);
    }

    [Fact]
    public async Task Revisions_GrowWithEverySaveEvenAnEditAndNeverRepeatAfterADelete()
    {
        await entries.Save(Day("2026-10-14"), "a", at, CancellationToken.None);
        await entries.Save(Day("2026-10-15"), "b", at, CancellationToken.None);
        await entries.Save(Day("2026-10-14"), "a edited", at, CancellationToken.None);
        Assert.Equal(3, await entries.LatestRevision(CancellationToken.None));

        Assert.True(await entries.Delete(Day("2026-10-14"), CancellationToken.None));
        Assert.Equal(3, await entries.LatestRevision(CancellationToken.None));
        await entries.Save(Day("2026-10-16"), "c", at, CancellationToken.None);

        Assert.Equal(4, await entries.LatestRevision(CancellationToken.None));
        Assert.Equal([("b", 2L), ("c", 4L)], (await entries.ListSince(0, 4, CancellationToken.None)).Select(revised => (revised.Entry.Text, revised.Revision)));
    }

    [Fact]
    public async Task ListSince_ReturnsOnlyEntriesAfterTheRevisionUpToTheLimitOrderedByRevision()
    {
        await entries.Save(Day("2026-10-16"), "one", at, CancellationToken.None);
        await entries.Save(Day("2026-10-10"), "two", at, CancellationToken.None);
        await entries.Save(Day("2026-10-12"), "three", at, CancellationToken.None);
        await entries.Save(Day("2026-10-16"), "one edited", at, CancellationToken.None);

        Assert.Equal(["two", "three"], (await entries.ListSince(1, 3, CancellationToken.None)).Select(revised => revised.Entry.Text));
        Assert.Equal(["two", "three", "one edited"], (await entries.ListSince(1, 4, CancellationToken.None)).Select(revised => revised.Entry.Text));
        Assert.Empty(await entries.ListSince(4, 4, CancellationToken.None));
    }

    [Fact]
    public async Task Entries_DeleteReportsWhetherTheDayHadOne()
    {
        await entries.Save(Day("2026-10-16"), "x", at, CancellationToken.None);

        Assert.True(await entries.Delete(Day("2026-10-16"), CancellationToken.None));
        Assert.False(await entries.Delete(Day("2026-10-16"), CancellationToken.None));
        Assert.Null(await entries.Get(Day("2026-10-16"), CancellationToken.None));
    }

    [Fact]
    public async Task Entries_ConcurrentSavesEachGetTheirOwnRevision()
    {
        await Task.WhenAll(Enumerable.Range(1, 20).Select(day => entries.Save(new DateOnly(2026, 10, day), $"day {day}", at, CancellationToken.None)));

        List<long> revisions = [.. (await entries.ListSince(0, 20, CancellationToken.None)).Select(revised => revised.Revision)];
        Assert.Equal(Enumerable.Range(1, 20).Select(revision => (long)revision), revisions);
    }

    private StoredSummary Stored(SummaryKind kind, string period, string start, string end, string text, long through) => new() { Summary = new Summary { Kind = kind, Period = period, Start = Day(start), End = Day(end), Text = text, EntryCount = 3, UpdatedAt = at }, ThroughRevision = through };

    [Fact]
    public async Task Summaries_SaveReplacesTheSamePeriodAndGetReadsItBack()
    {
        await summaries.Save(Stored(SummaryKind.Week, "2026-10-12", "2026-10-12", "2026-10-18", "first", 2), CancellationToken.None);
        await summaries.Save(Stored(SummaryKind.Week, "2026-10-12", "2026-10-12", "2026-10-18", "second", 5), CancellationToken.None);

        StoredSummary stored = (await summaries.Get(SummaryKind.Week, "2026-10-12", CancellationToken.None))!;
        Assert.Equal(("second", 5L, 3), (stored.Summary.Text, stored.ThroughRevision, stored.Summary.EntryCount));
        Assert.Equal(at, stored.Summary.UpdatedAt);
        Assert.Equal(Day("2026-10-18"), stored.Summary.End);
        Assert.Single(await summaries.List(null, CancellationToken.None));
        Assert.Null(await summaries.Get(SummaryKind.Month, "2026-10-12", CancellationToken.None));
    }

    [Fact]
    public async Task Summaries_ListFiltersByKindAndOrdersNewestPeriodFirst()
    {
        await summaries.Save(Stored(SummaryKind.Week, "2026-10-05", "2026-10-05", "2026-10-11", "old week", 1), CancellationToken.None);
        await summaries.Save(Stored(SummaryKind.Week, "2026-10-12", "2026-10-12", "2026-10-18", "new week", 2), CancellationToken.None);
        await summaries.Save(Stored(SummaryKind.Month, "2026-10", "2026-10-01", "2026-10-31", "month", 2), CancellationToken.None);

        Assert.Equal(["new week", "old week"], (await summaries.List(SummaryKind.Week, CancellationToken.None)).Select(summary => summary.Text));
        Assert.Equal(["month"], (await summaries.List(SummaryKind.Month, CancellationToken.None)).Select(summary => summary.Text));
        Assert.Empty(await summaries.List(SummaryKind.Year, CancellationToken.None));
        Assert.Equal(3, (await summaries.List(null, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Summaries_RememberProgressAndTheLastRun()
    {
        Assert.Equal(0, await summaries.GetSummarizedRevision(CancellationToken.None));
        Assert.Null(await summaries.GetLastRun(CancellationToken.None));

        await summaries.SetSummarizedRevision(42, CancellationToken.None);
        await summaries.SetLastRun(at, CancellationToken.None);

        Assert.Equal(42, await summaries.GetSummarizedRevision(CancellationToken.None));
        Assert.Equal(at, await summaries.GetLastRun(CancellationToken.None));
    }

    [Fact]
    public async Task Chat_MessagesKeepTheirOrderRolesAndText()
    {
        ChatMessage question = await chats.Add(ChatRole.User, "What did I do?", at, CancellationToken.None);
        ChatMessage answer = await chats.Add(ChatRole.Assistant, "You ran.", at.AddSeconds(5), CancellationToken.None);

        Assert.True(answer.Id > question.Id);
        IReadOnlyList<ChatMessage> messages = await chats.List(CancellationToken.None);
        Assert.Equal([(ChatRole.User, "What did I do?"), (ChatRole.Assistant, "You ran.")], messages.Select(message => (message.Role, message.Text)));
        Assert.Equal(question.Id, messages[0].Id);
        Assert.Equal(at.AddSeconds(5), messages[1].At);
    }

    [Fact]
    public async Task Chat_ClearDeletesEveryMessage()
    {
        await chats.Add(ChatRole.User, "x", at, CancellationToken.None);

        await chats.Clear(CancellationToken.None);

        Assert.Empty(await chats.List(CancellationToken.None));
    }

    [Fact]
    public async Task Chat_DeleteBeforeRemovesOnlyOlderMessagesWhateverOffsetTheyWereWrittenWith()
    {
        DateTimeOffset cutoff = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        await chats.Add(ChatRole.User, "old in another zone", cutoff.ToOffset(TimeSpan.FromHours(-7)).AddMinutes(-1), CancellationToken.None);
        await chats.Add(ChatRole.Assistant, "old", cutoff.AddDays(-3), CancellationToken.None);
        await chats.Add(ChatRole.User, "exactly at the cutoff", cutoff, CancellationToken.None);
        await chats.Add(ChatRole.Assistant, "new in another zone", cutoff.ToOffset(TimeSpan.FromHours(9)).AddMinutes(1), CancellationToken.None);

        Assert.Equal(2, await chats.DeleteBefore(cutoff, CancellationToken.None));

        Assert.Equal(["exactly at the cutoff", "new in another zone"], (await chats.List(CancellationToken.None)).Select(message => message.Text));
        Assert.Equal(0, await chats.DeleteBefore(cutoff, CancellationToken.None));
    }

    [Fact]
    public async Task Meta_SetReplacesAndRemoveForgets()
    {
        Assert.Null(await meta.Get("k", CancellationToken.None));

        await meta.Set("k", "one", CancellationToken.None);
        await meta.Set("k", "two", CancellationToken.None);
        Assert.Equal("two", await meta.Get("k", CancellationToken.None));

        await meta.Remove("k", CancellationToken.None);
        Assert.Null(await meta.Get("k", CancellationToken.None));
    }
}
