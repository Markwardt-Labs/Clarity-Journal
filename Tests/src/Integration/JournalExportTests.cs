namespace Markwardt.ClarityJournal.Tests.Integration;

public sealed class JournalExportTests : IAsyncLifetime
{
    private readonly TempFolder temp = new();

    private readonly Database database = new();

    private readonly DateTimeOffset at = new(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);

    private EntryStore entries = null!;

    private SummaryStore summaries = null!;

    private JournalExport export = null!;

    public async Task InitializeAsync()
    {
        await database.Initialize(temp.Path, CancellationToken.None);
        entries = new EntryStore(database);
        summaries = new SummaryStore(database, new MetaStore(database));
        export = new JournalExport(entries, summaries);
    }

    public Task DisposeAsync()
    {
        temp.Dispose();
        return Task.CompletedTask;
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(export.Folder, relative));

    private Summary Summary(SummaryKind kind, string period, string start, string end) => new() { Kind = kind, Period = period, Start = DateOnly.Parse(start, CultureInfo.InvariantCulture), End = DateOnly.Parse(end, CultureInfo.InvariantCulture), Text = $"{kind} summary text", EntryCount = 1, UpdatedAt = at };

    [Fact]
    public async Task WriteEntry_CreatesOneMarkdownFilePerDayUnderItsYear()
    {
        await export.Open(temp.Path, CancellationToken.None);

        await export.WriteEntry(new JournalEntry { Date = new DateOnly(2026, 10, 16), Text = "A good day.", UpdatedAt = at }, CancellationToken.None);

        Assert.Equal(Path.Combine(temp.Path, "export"), export.Folder);
        Assert.Equal("# Friday, October 16, 2026\n\nA good day.\n", Read("entries/2026/2026-10-16.md"));
    }

    [Fact]
    public async Task WriteEntry_RewritesTheFileWhenTheEntryChangesAndDeleteRemovesIt()
    {
        await export.Open(temp.Path, CancellationToken.None);
        DateOnly date = new(2026, 10, 16);

        await export.WriteEntry(new JournalEntry { Date = date, Text = "first", UpdatedAt = at }, CancellationToken.None);
        await export.WriteEntry(new JournalEntry { Date = date, Text = "second", UpdatedAt = at }, CancellationToken.None);
        Assert.Contains("second", Read("entries/2026/2026-10-16.md"));
        Assert.Empty(Directory.GetFiles(export.Folder, "*.tmp", SearchOption.AllDirectories));

        export.DeleteEntry(date);
        Assert.False(File.Exists(Path.Combine(export.Folder, "entries", "2026", "2026-10-16.md")));
    }

    [Theory]
    [InlineData(SummaryKind.Week, "2026-10-12", "2026-10-12", "2026-10-18", "summaries/weeks/2026-10-12.md", "# Summary of the week 2026-10-12 to 2026-10-18")]
    [InlineData(SummaryKind.Month, "2026-10", "2026-10-01", "2026-10-31", "summaries/months/2026-10.md", "# Summary of October 2026")]
    [InlineData(SummaryKind.Year, "2026", "2026-01-01", "2026-12-31", "summaries/years/2026.md", "# Summary of 2026")]
    [InlineData(SummaryKind.Lifetime, "lifetime", "2026-03-05", "2026-10-16", "summaries/lifetime.md", "# Summary of the whole journal")]
    public async Task WriteSummary_PutsEachKindInItsOwnFolderWithATitle(SummaryKind kind, string period, string start, string end, string path, string title)
    {
        await export.Open(temp.Path, CancellationToken.None);

        await export.WriteSummary(Summary(kind, period, start, end), CancellationToken.None);

        Assert.Equal($"{title}\n\n{kind} summary text\n", Read(path));
    }

    [Fact]
    public async Task Open_RebuildsEverythingFromTheDatabaseAndDropsStrayFiles()
    {
        await entries.Save(new DateOnly(2026, 10, 16), "from the database", at, CancellationToken.None);
        await summaries.Save(new StoredSummary { Summary = Summary(SummaryKind.Year, "2026", "2026-01-01", "2026-12-31"), ThroughRevision = 1 }, CancellationToken.None);
        Directory.CreateDirectory(Path.Combine(temp.Path, "export", "entries", "1999"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "export", "entries", "1999", "1999-01-01.md"), "stale");

        await export.Open(temp.Path, CancellationToken.None);

        Assert.Contains("from the database", Read("entries/2026/2026-10-16.md"));
        Assert.Contains("Year summary text", Read("summaries/years/2026.md"));
        Assert.False(File.Exists(Path.Combine(export.Folder, "entries", "1999", "1999-01-01.md")));
    }
}
