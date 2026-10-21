namespace Markwardt.ClarityJournal;

/// <summary>
/// The journal entry of one day. A day has at most one entry, so <see cref="Date" /> identifies it.
/// </summary>
public sealed record JournalEntry
{
    /// <summary>
    /// Gets the day the entry is about, as the client that wrote it chose it.
    /// </summary>
    public required DateOnly Date { get; init; }

    /// <summary>
    /// Gets the entry text.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets when the entry was last saved.
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
