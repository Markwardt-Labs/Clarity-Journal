namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// Reads and writes the journal entries, keeping the files Claude Code reads and the clients in step.
/// </summary>
internal interface IJournalService
{
    /// <summary>
    /// Lists the entries between two days.
    /// </summary>
    /// <param name="from">The first day, or <see langword="null" /> for no lower bound.</param>
    /// <param name="to">The last day, or <see langword="null" /> for no upper bound.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The entries, oldest first.</returns>
    Task<IReadOnlyList<JournalEntry>> List(DateOnly? from, DateOnly? to, CancellationToken cancellation);

    /// <summary>
    /// Reads the entry of a day.
    /// </summary>
    /// <param name="date">The day.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The entry, or <see langword="null" /> if the day has none.</returns>
    Task<JournalEntry?> Get(DateOnly date, CancellationToken cancellation);

    /// <summary>
    /// Writes the entry of a day, replacing the one it has.
    /// </summary>
    /// <param name="date">The day, which may be at most one day ahead of the server's today, to allow for time zones.</param>
    /// <param name="text">The entry text; it must not be blank.</param>
    /// <param name="cancellation">Cancels the write.</param>
    /// <returns>The stored entry.</returns>
    /// <exception cref="RequestException">The text is blank or too long, or the day is too far ahead.</exception>
    Task<JournalEntry> Save(DateOnly date, string text, CancellationToken cancellation);

    /// <summary>
    /// Deletes the entry of a day.
    /// </summary>
    /// <param name="date">The day.</param>
    /// <param name="cancellation">Cancels the write.</param>
    /// <exception cref="RequestException">The day has no entry.</exception>
    Task Delete(DateOnly date, CancellationToken cancellation);
}

/// <inheritdoc />
internal sealed class JournalService(IEntryStore entries, IJournalExport export, IEventHub events, TimeProvider time) : IJournalService
{
    private readonly int maxLength = 100000;

    /// <inheritdoc />
    public Task<IReadOnlyList<JournalEntry>> List(DateOnly? from, DateOnly? to, CancellationToken cancellation) => entries.List(from, to, cancellation);

    /// <inheritdoc />
    public Task<JournalEntry?> Get(DateOnly date, CancellationToken cancellation) => entries.Get(date, cancellation);

    /// <inheritdoc />
    public async Task<JournalEntry> Save(DateOnly date, string text, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new RequestException("The entry is empty.");
        }

        if (text.Length > maxLength)
        {
            throw new RequestException($"The entry is longer than {maxLength} characters.");
        }

        if (date > DateOnly.FromDateTime(time.GetLocalNow().DateTime).AddDays(1))
        {
            throw new RequestException("An entry cannot be written for a day that has not started.");
        }

        JournalEntry entry = await entries.Save(date, text.Trim(), time.GetUtcNow(), cancellation);
        await export.WriteEntry(entry, cancellation);
        events.Publish(new RemoteEvent { Kind = RemoteEventKind.EntryChanged, Date = date });
        return entry;
    }

    /// <inheritdoc />
    public async Task Delete(DateOnly date, CancellationToken cancellation)
    {
        if (!await entries.Delete(date, cancellation))
        {
            throw new RequestException("That day has no entry.");
        }

        export.DeleteEntry(date);
        events.Publish(new RemoteEvent { Kind = RemoteEventKind.EntryChanged, Date = date });
    }
}
