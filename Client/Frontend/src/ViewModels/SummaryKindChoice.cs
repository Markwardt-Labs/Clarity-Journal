namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// One segment of the control that picks which kind of summary is shown.
/// </summary>
/// <param name="kind">The kind of summary the segment shows.</param>
/// <param name="label">The text of the segment.</param>
/// <param name="select">Selects the segment.</param>
internal sealed class SummaryKindChoice(SummaryKind kind, string label, ICommand select) : ViewModelBase
{
    private bool isSelected;

    /// <summary>
    /// Gets the kind of summary the segment shows.
    /// </summary>
    public SummaryKind Kind { get; } = kind;

    /// <summary>
    /// Gets the text of the segment.
    /// </summary>
    public string Label { get; } = label;

    /// <summary>
    /// Gets or sets a value indicating whether the segment is the one selected.
    /// </summary>
    public bool IsSelected
    {
        get => isSelected;
        set => Set(ref isSelected, value);
    }

    /// <summary>
    /// Gets the command that selects the segment.
    /// </summary>
    public ICommand Select { get; } = select;
}
