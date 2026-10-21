namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// A summary together with how far into the journal it has read.
/// </summary>
internal sealed record StoredSummary
{
    /// <summary>
    /// Gets the summary the clients see.
    /// </summary>
    public required Summary Summary { get; init; }

    /// <summary>
    /// Gets the revision through which the summary has taken in every entry of its period, so only entries saved after it still need to be read.
    /// </summary>
    public required long ThroughRevision { get; init; }
}
