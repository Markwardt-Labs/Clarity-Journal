namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// An entry together with the revision number of the save that wrote it. Revisions grow with every save, so they order the entries by when they were last written.
/// </summary>
internal sealed record RevisedEntry
{
    /// <summary>
    /// Gets the entry.
    /// </summary>
    public required JournalEntry Entry { get; init; }

    /// <summary>
    /// Gets the revision of the save that wrote the entry.
    /// </summary>
    public required long Revision { get; init; }
}
