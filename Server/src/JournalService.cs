namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// Reads and writes the journal entries, keeping the files Claude Code reads and the clients in step.
/// </summary>
internal interface IJournalService
{
    /// <summary>
    /// Lists what a list of entries shows of the days that have one, newest first, one page at a time.
    /// </summary>
    /// <param name="before">The day before which days are listed, or <see langword="null" /> to start with the newest.</param>
    /// <param name="limit">How many days at most, or <see langword="null" /> for 50; at least 1 and at most 200.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The digests, newest first; fewer than the limit means there are no older days.</returns>
    Task<IReadOnlyList<EntryDigest>> Digests(DateOnly? before, int? limit, CancellationToken cancellation = default);

    /// <summary>
    /// Reads the entry of a day.
    /// </summary>
    /// <param name="date">The day.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The entry, or <see langword="null" /> if the day has none.</returns>
    Task<JournalEntry?> Get(DateOnly date, CancellationToken cancellation = default);

    /// <summary>
    /// Writes the entry of a day, replacing the one it has.
    /// </summary>
    /// <param name="date">The day, which may be at most one day ahead of the server's today, to allow for time zones.</param>
    /// <param name="text">The entry text; it must not be blank.</param>
    /// <param name="cancellation">Cancels the write.</param>
    /// <returns>The stored entry.</returns>
    /// <exception cref="RequestException">The text is blank or too long, or the day is too far ahead.</exception>
    Task<JournalEntry> Save(DateOnly date, string text, CancellationToken cancellation = default);

    /// <summary>
    /// Deletes the entry of a day.
    /// </summary>
    /// <param name="date">The day.</param>
    /// <param name="cancellation">Cancels the write.</param>
    /// <exception cref="RequestException">The day has no entry.</exception>
    Task Delete(DateOnly date, CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class JournalService(IEntryStore entries, IJournalExport export, IEventHub events, TimeProvider time) : IJournalService
{
    private readonly int maxLength = 100000;

    private readonly int defaultPage = 50;

    private readonly int maxPage = 200;

    /// <inheritdoc />
    public Task<IReadOnlyList<EntryDigest>> Digests(DateOnly? before, int? limit, CancellationToken cancellation = default)
        => entries.ListDigests(before, Math.Clamp(limit ?? defaultPage, 1, maxPage), cancellation);

    /// <inheritdoc />
    public Task<JournalEntry?> Get(DateOnly date, CancellationToken cancellation = default)
        => entries.Get(date, cancellation);

    /// <inheritdoc />
    public async Task<JournalEntry> Save(DateOnly date, string text, CancellationToken cancellation = default)
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
        events.Publish(new RemoteEvent { Kind = RemoteEventKind.EntryChanged, Date = date, Digest = await entries.GetDigest(date, cancellation) });
        return entry;
    }

    /// <inheritdoc />
    public async Task Delete(DateOnly date, CancellationToken cancellation = default)
    {
        if (!await entries.Delete(date, cancellation))
        {
            throw new RequestException("That day has no entry.");
        }

        export.DeleteEntry(date);
        events.Publish(new RemoteEvent { Kind = RemoteEventKind.EntryChanged, Date = date });
    }
}
