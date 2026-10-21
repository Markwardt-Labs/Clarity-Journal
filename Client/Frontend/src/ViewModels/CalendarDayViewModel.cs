namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// One day cell of the month calendar.
/// </summary>
/// <param name="date">The day.</param>
/// <param name="inMonth">Whether the day belongs to the month shown, as opposed to a neighbouring month filling the grid.</param>
/// <param name="hasEntry">Whether the day has an entry.</param>
/// <param name="isToday">Whether the day is today.</param>
/// <param name="isSelected">Whether the day is the one shown.</param>
/// <param name="isSelectable">Whether the day can be selected: today, or an earlier day that has an entry.</param>
/// <param name="select">Selects the day.</param>
internal sealed class CalendarDayViewModel(DateOnly date, bool inMonth, bool hasEntry, bool isToday, bool isSelected, bool isSelectable, ICommand select)
{
    /// <summary>
    /// Gets the day.
    /// </summary>
    public DateOnly Date { get; } = date;

    /// <summary>
    /// Gets the day of the month as shown in the cell.
    /// </summary>
    public string Label { get; } = date.Day.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets a value indicating whether the day belongs to the month shown.
    /// </summary>
    public bool InMonth { get; } = inMonth;

    /// <summary>
    /// Gets a value indicating whether the day has an entry.
    /// </summary>
    public bool HasEntry { get; } = hasEntry;

    /// <summary>
    /// Gets a value indicating whether the day is today.
    /// </summary>
    public bool IsToday { get; } = isToday;

    /// <summary>
    /// Gets a value indicating whether the day is the one shown.
    /// </summary>
    public bool IsSelected { get; } = isSelected;

    /// <summary>
    /// Gets a value indicating whether the day can be selected: today, or an earlier day that has an entry.
    /// </summary>
    public bool IsSelectable { get; } = isSelectable;

    /// <summary>
    /// Gets the command that selects the day.
    /// </summary>
    public ICommand Select { get; } = select;
}
