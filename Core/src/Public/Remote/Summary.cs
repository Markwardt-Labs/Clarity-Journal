namespace Markwardt.ClarityJournal;

/// <summary>
/// A summary of the journal over a week, month, year or the whole lifetime, written by Claude Code on the server.
/// </summary>
public sealed record Summary
{
    /// <summary>
    /// Gets the kind of period the summary covers.
    /// </summary>
    public required SummaryKind Kind { get; init; }

    /// <summary>
    /// Gets the identifier of the period, which together with <see cref="Kind" /> identifies the summary; see <see cref="SummaryPeriod.Id" />.
    /// </summary>
    public required string Period { get; init; }

    /// <summary>
    /// Gets the first day the summary covers; for a lifetime summary, the day of the first entry.
    /// </summary>
    public required DateOnly Start { get; init; }

    /// <summary>
    /// Gets the last day the summary covers; for a lifetime summary, the day of the last entry.
    /// </summary>
    public required DateOnly End { get; init; }

    /// <summary>
    /// Gets the summary text, as markdown.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets how many entries the summary has taken in.
    /// </summary>
    public required int EntryCount { get; init; }

    /// <summary>
    /// Gets when the summary was last updated.
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
