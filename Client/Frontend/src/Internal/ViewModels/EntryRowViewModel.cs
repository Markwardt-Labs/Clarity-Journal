namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// One row of the list of entries: the date, the generated title and the summary in a compact form.
/// </summary>
internal sealed class EntryRowViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntryRowViewModel" /> class.
    /// </summary>
    /// <param name="date">The day of the row.</param>
    /// <param name="today">The current day on the device.</param>
    /// <param name="isSelected">Whether the day is the one the screen shows.</param>
    /// <param name="select">Shows the day.</param>
    public EntryRowViewModel(DateOnly date, DateOnly today, bool isSelected, ICommand select)
    {
        Date = date;
        IsToday = date == today;
        DateLabel = IsToday ? "Today" : date.ToString(date.Year == today.Year ? "ddd, MMM d" : "ddd, MMM d, yyyy", CultureInfo.InvariantCulture);
        isSelectedRow = isSelected;
        Select = select;
        Update(null);
    }

    private bool isSelectedRow;

    private string heading = string.Empty;

    private string body = string.Empty;

    /// <summary>
    /// Gets the day of the row.
    /// </summary>
    public DateOnly Date { get; }

    /// <summary>
    /// Gets the date as the row shows it: <c>Today</c>, or a short date such as <c>Fri, Oct 16</c> with the year when it is not this year's.
    /// </summary>
    public string DateLabel { get; }

    /// <summary>
    /// Gets a value indicating whether the row is today's, which is always in the list so that the entry being written can be returned to even before it has any text.
    /// </summary>
    public bool IsToday { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the day is the one the screen shows.
    /// </summary>
    public bool IsSelected
    {
        get => isSelectedRow;
        set => Set(ref isSelectedRow, value);
    }

    /// <summary>
    /// Gets the generated title, or an empty string while the entry has none.
    /// </summary>
    public string Heading
    {
        get => heading;
        private set
        {
            if (Set(ref heading, value))
            {
                Raise(nameof(HasHeading));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the row has a title to show.
    /// </summary>
    public bool HasHeading => heading.Length > 0;

    /// <summary>
    /// Gets the generated summary; while the entry has none, the start of its text; for today without an entry, an invitation to write.
    /// </summary>
    public string Body
    {
        get => body;
        private set => Set(ref body, value);
    }

    /// <summary>
    /// Gets the command that shows the day.
    /// </summary>
    public ICommand Select { get; }

    /// <summary>
    /// Takes up what the server now says about the day.
    /// </summary>
    /// <param name="digest">The digest, or <see langword="null" /> if the day has no entry.</param>
    public void Update(EntryDigest? digest)
    {
        if (digest is null)
        {
            Heading = string.Empty;
            Body = IsToday ? "Tap to write today's entry" : string.Empty;
            return;
        }

        bool titled = !string.IsNullOrWhiteSpace(digest.Title) && !string.IsNullOrWhiteSpace(digest.Summary);
        Heading = titled ? digest.Title! : string.Empty;
        Body = titled ? digest.Summary! : digest.Excerpt;
    }
}
