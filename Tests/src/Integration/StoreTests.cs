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

    [Fact]
    public async Task Digests_ListNewestFirstOnePageAtATimeWithTheStartOfTheText()
    {
        foreach (string date in new[] { "2026-10-12", "2026-10-14", "2026-10-13", "2026-10-15" })
        {
            await entries.Save(Day(date), $"Entry of {date}\n\nwith   a second line", at, CancellationToken.None);
        }

        IReadOnlyList<EntryDigest> first = await entries.ListDigests(null, 2, CancellationToken.None);
        IReadOnlyList<EntryDigest> second = await entries.ListDigests(first[^1].Date, 2, CancellationToken.None);

        Assert.Equal([Day("2026-10-15"), Day("2026-10-14")], first.Select(digest => digest.Date));
        Assert.Equal([Day("2026-10-13"), Day("2026-10-12")], second.Select(digest => digest.Date));
        Assert.Equal("Entry of 2026-10-15 with a second line", first[0].Excerpt);
        Assert.All(first.Concat(second), digest => Assert.True(digest.Title is null && digest.Summary is null));
        Assert.Empty(await entries.ListDigests(Day("2026-10-12"), 50, CancellationToken.None));
    }

    [Fact]
    public async Task Digests_LongTextIsCutToAShortExcerpt()
    {
        await entries.Save(Day("2026-10-15"), string.Join(' ', Enumerable.Repeat("word", 400)), at, CancellationToken.None);

        EntryDigest digest = (await entries.GetDigest(Day("2026-10-15"), CancellationToken.None))!;

        Assert.EndsWith("...", digest.Excerpt, StringComparison.Ordinal);
        Assert.InRange(digest.Excerpt.Length, 100, 163);
        Assert.Null(await entries.GetDigest(Day("2026-10-01"), CancellationToken.None));
    }

    [Fact]
    public async Task SetDigest_StoresTheTitleAndSummaryAndShowsThemInTheDigest()
    {
        await entries.Save(Day("2026-10-15"), "Cooked pasta", at, CancellationToken.None);

        Assert.True(await entries.SetDigest(Day("2026-10-15"), "Cooked pasta", "Pasta night", "You cooked for friends.", CancellationToken.None));

        EntryDigest digest = (await entries.GetDigest(Day("2026-10-15"), CancellationToken.None))!;
        Assert.Equal(("Pasta night", "You cooked for friends.", "Cooked pasta"), (digest.Title, digest.Summary, digest.Excerpt));
        Assert.Equal("Pasta night", (await entries.ListDigests(null, 10, CancellationToken.None)).Single().Title);
    }

    [Fact]
    public async Task SetDigest_IsRefusedWhenTheTextChangedWhileTheTitleWasBeingMade()
    {
        await entries.Save(Day("2026-10-15"), "Old words", at, CancellationToken.None);
        await entries.Save(Day("2026-10-15"), "New words", at, CancellationToken.None);

        Assert.False(await entries.SetDigest(Day("2026-10-15"), "Old words", "Stale", "Stale.", CancellationToken.None));
        Assert.False(await entries.SetDigest(Day("2026-10-01"), "x", "No entry", "No entry.", CancellationToken.None));

        Assert.Null((await entries.GetDigest(Day("2026-10-15"), CancellationToken.None))!.Title);
    }

    [Fact]
    public async Task SetDigest_IsNotASaveSoSummariesDoNotReadTheEntryAgain()
    {
        await entries.Save(Day("2026-10-15"), "Cooked pasta", at, CancellationToken.None);
        long revision = await entries.LatestRevision(CancellationToken.None);

        await entries.SetDigest(Day("2026-10-15"), "Cooked pasta", "Pasta night", "You cooked.", CancellationToken.None);

        Assert.Equal(revision, await entries.LatestRevision(CancellationToken.None));
        Assert.Equal(revision, Assert.Single(await entries.ListSince(0, revision, CancellationToken.None)).Revision);
    }

    [Fact]
    public async Task Save_ChangedTextClearsTheTitleButTheSameTextKeepsIt()
    {
        await entries.Save(Day("2026-10-15"), "Cooked pasta", at, CancellationToken.None);
        await entries.SetDigest(Day("2026-10-15"), "Cooked pasta", "Pasta night", "You cooked.", CancellationToken.None);

        await entries.Save(Day("2026-10-15"), "Cooked pasta", at.AddMinutes(1), CancellationToken.None);
        Assert.Equal("Pasta night", (await entries.GetDigest(Day("2026-10-15"), CancellationToken.None))!.Title);

        await entries.Save(Day("2026-10-15"), "Cooked pasta and baked bread", at.AddMinutes(2), CancellationToken.None);
        EntryDigest digest = (await entries.GetDigest(Day("2026-10-15"), CancellationToken.None))!;
        Assert.Null(digest.Title);
        Assert.Null(digest.Summary);
    }

    [Fact]
    public async Task ListUntitled_IsDaysBeforeTheGivenDayWithoutBothTitleAndSummaryNewestFirst()
    {
        foreach (string date in new[] { "2026-10-12", "2026-10-13", "2026-10-14", "2026-10-15", "2026-10-16" })
        {
            await entries.Save(Day(date), $"Text {date}", at, CancellationToken.None);
        }

        await entries.SetDigest(Day("2026-10-13"), "Text 2026-10-13", "Done", "Done.", CancellationToken.None);

        IReadOnlyList<JournalEntry> untitled = await entries.ListUntitled(Day("2026-10-16"), CancellationToken.None);

        Assert.Equal([Day("2026-10-15"), Day("2026-10-14"), Day("2026-10-12")], untitled.Select(entry => entry.Date));
        Assert.Equal("Text 2026-10-15", untitled[0].Text);
    }

    [Fact]
    public async Task Initialize_AddsTheTitleColumnsToADatabaseMadeByAnOlderVersionKeepingItsEntries()
    {
        using TempFolder old = new();
        string path = Path.Combine(old.Path, "journal.db");

        await using (SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            await connection.OpenAsync();
            await using SqliteCommand create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE entries (date TEXT NOT NULL PRIMARY KEY, text TEXT NOT NULL, revision INTEGER NOT NULL, updated_at TEXT NOT NULL);
                CREATE TABLE meta (key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO meta (key, value) VALUES ('revision', '2');
                INSERT INTO entries VALUES ('2026-10-23', 'An entry from before titles existed', 1, '2026-10-23T20:00:00.0000000+00:00');
                INSERT INTO entries VALUES ('2026-10-24', 'Another old entry', 2, '2026-10-24T20:00:00.0000000+00:00');
                """;
            await create.ExecuteNonQueryAsync();
        }

        Database migrated = new();
        await migrated.Initialize(old.Path, CancellationToken.None);
        await migrated.Initialize(old.Path, CancellationToken.None);
        EntryStore migratedEntries = new(migrated);

        Assert.Equal("An entry from before titles existed", (await migratedEntries.Get(Day("2026-10-23"), CancellationToken.None))!.Text);
        Assert.Equal(2, await migratedEntries.LatestRevision(CancellationToken.None));
        Assert.Equal([Day("2026-10-24"), Day("2026-10-23")], (await migratedEntries.ListUntitled(Day("2026-11-06"), CancellationToken.None)).Select(entry => entry.Date));
        Assert.True(await migratedEntries.SetDigest(Day("2026-10-23"), "An entry from before titles existed", "Before titles", "You wrote before titles.", CancellationToken.None));
        Assert.Equal("Before titles", (await migratedEntries.GetDigest(Day("2026-10-23"), CancellationToken.None))!.Title);
        await migratedEntries.Save(Day("2026-10-25"), "A new entry", at, CancellationToken.None);
        Assert.Equal(3, await migratedEntries.LatestRevision(CancellationToken.None));
    }
}
