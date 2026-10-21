namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// Keeps a folder of plain markdown files mirroring the journal, which is what Claude Code reads to answer chat questions: its read-only tools work on files, not on the database.
/// </summary>
internal interface IJournalExport
{
    /// <summary>
    /// Gets the folder holding the files.
    /// </summary>
    string Folder { get; }

    /// <summary>
    /// Sets the folder and rewrites every file from the database, so the files never drift from it.
    /// </summary>
    /// <param name="dataFolder">The server's data folder; the files go in its <c>export</c> folder.</param>
    /// <param name="cancellation">Cancels the rebuild.</param>
    Task Open(string dataFolder, CancellationToken cancellation);

    /// <summary>
    /// Writes the file of an entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="cancellation">Cancels the write.</param>
    Task WriteEntry(JournalEntry entry, CancellationToken cancellation);

    /// <summary>
    /// Deletes the file of an entry.
    /// </summary>
    /// <param name="date">The day of the entry.</param>
    void DeleteEntry(DateOnly date);

    /// <summary>
    /// Writes the file of a summary.
    /// </summary>
    /// <param name="summary">The summary.</param>
    /// <param name="cancellation">Cancels the write.</param>
    Task WriteSummary(Summary summary, CancellationToken cancellation);
}

/// <inheritdoc />
internal sealed class JournalExport(IEntryStore entries, ISummaryStore summaries) : IJournalExport
{
    private readonly SemaphoreSlim writing = new(1, 1);

    /// <inheritdoc />
    public string Folder { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async Task Open(string dataFolder, CancellationToken cancellation)
    {
        Folder = Path.Combine(dataFolder, "export");

        if (Directory.Exists(Folder))
        {
            Directory.Delete(Folder, true);
        }

        Directory.CreateDirectory(Folder);

        foreach (JournalEntry entry in await entries.List(null, null, cancellation))
        {
            await WriteEntry(entry, cancellation);
        }

        foreach (Summary summary in await summaries.List(null, cancellation))
        {
            await WriteSummary(summary, cancellation);
        }
    }

    /// <inheritdoc />
    public Task WriteEntry(JournalEntry entry, CancellationToken cancellation) => Write(EntryPath(entry.Date), $"# {entry.Date.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture)}\n\n{entry.Text}\n", cancellation);

    /// <inheritdoc />
    public void DeleteEntry(DateOnly date) => File.Delete(EntryPath(date));

    /// <inheritdoc />
    public Task WriteSummary(Summary summary, CancellationToken cancellation) => Write(SummaryPath(summary), $"# {Title(summary)}\n\n{summary.Text}\n", cancellation);

    private string EntryPath(DateOnly date) => Path.Combine(Folder, "entries", date.Year.ToString("0000", CultureInfo.InvariantCulture), $"{date:yyyy-MM-dd}.md");

    private string SummaryPath(Summary summary) => summary.Kind switch
    {
        SummaryKind.Week => Path.Combine(Folder, "summaries", "weeks", $"{summary.Period}.md"),
        SummaryKind.Month => Path.Combine(Folder, "summaries", "months", $"{summary.Period}.md"),
        SummaryKind.Year => Path.Combine(Folder, "summaries", "years", $"{summary.Period}.md"),
        _ => Path.Combine(Folder, "summaries", "lifetime.md")
    };

    private string Title(Summary summary) => summary.Kind switch
    {
        SummaryKind.Week => $"Summary of the week {summary.Start:yyyy-MM-dd} to {summary.End:yyyy-MM-dd}",
        SummaryKind.Month => $"Summary of {summary.Start.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}",
        SummaryKind.Year => $"Summary of {summary.Period}",
        _ => "Summary of the whole journal"
    };

    private async Task Write(string path, string content, CancellationToken cancellation)
    {
        await writing.WaitAsync(cancellation);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, content, cancellation);
            File.Move(temporary, path, true);
        }
        finally
        {
            writing.Release();
        }
    }
}
