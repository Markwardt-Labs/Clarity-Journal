namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// One summary shown as a card.
/// </summary>
internal sealed class SummaryItemViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SummaryItemViewModel" /> class.
    /// </summary>
    /// <param name="summary">The summary to show.</param>
    public SummaryItemViewModel(Summary summary)
    {
        Title = summary.Kind switch
        {
            SummaryKind.Week => $"{Format(summary.Start, "MMM d")} to {Format(summary.End, "MMM d, yyyy")}",
            SummaryKind.Month => Format(summary.Start, "MMMM yyyy"),
            SummaryKind.Year => summary.Period,
            _ => "Life"
        };
        Subtitle = $"{summary.EntryCount} {(summary.EntryCount == 1 ? "entry" : "entries")}, updated {summary.UpdatedAt.ToLocalTime().ToString("MMM d", CultureInfo.InvariantCulture)}";
        Text = summary.Text;
    }

    /// <summary>
    /// Gets the period the summary covers, such as <c>Oct 12 to Oct 18, 2026</c>.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets how many entries the period has and when the summary was last updated.
    /// </summary>
    public string Subtitle { get; }

    /// <summary>
    /// Gets the summary text, as markdown.
    /// </summary>
    public string Text { get; }

    private static string Format(DateOnly date, string format) => date.ToString(format, CultureInfo.InvariantCulture);
}
