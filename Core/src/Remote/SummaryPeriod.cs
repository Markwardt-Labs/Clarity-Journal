namespace Markwardt.ClarityJournal;

/// <summary>
/// One calendar period a summary covers, such as a particular week or month.
/// </summary>
public sealed record SummaryPeriod
{
    /// <summary>
    /// Gets the kind of period.
    /// </summary>
    public required SummaryKind Kind { get; init; }

    /// <summary>
    /// Gets the stable identifier of the period: the Monday of a week as <c>yyyy-MM-dd</c>, a month as <c>yyyy-MM</c>, a year as <c>yyyy</c>, and <c>lifetime</c> for the lifetime.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the first day of the period.
    /// </summary>
    public required DateOnly Start { get; init; }

    /// <summary>
    /// Gets the last day of the period.
    /// </summary>
    public required DateOnly End { get; init; }
}
