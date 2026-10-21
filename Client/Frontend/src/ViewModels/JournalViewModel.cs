namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// The journal screen: today's entry, written and saved here, and a calendar that opens on demand to read the entry of an earlier day.
/// </summary>
internal sealed class JournalViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JournalViewModel" /> class.
    /// </summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="today">Gives the current day on the device.</param>
    /// <param name="recorder">Records speech for voice input, or <see langword="null" /> when this client cannot record.</param>
    /// <param name="time">Times the automatic saving, or <see langword="null" /> for the system clock.</param>
    public JournalViewModel(IServerConnection connection, Func<DateOnly> today, IVoiceRecorder? recorder = null, TimeProvider? time = null)
    {
        this.connection = connection;
        this.today = today;
        this.time = time ?? TimeProvider.System;
        selected = today();
        month = new DateOnly(selected.Year, selected.Month, 1);
        ToggleCalendarCommand = new AsyncCommand(_ => ToggleCalendar());
        PreviousMonthCommand = new AsyncCommand(_ => ChangeMonth(-1));
        NextMonthCommand = new AsyncCommand(_ => ChangeMonth(1));
        Dictation = new Dictation(connection, recorder, () => CanEdit);
        Dictation.Dictated += text => EntryText = entryText.Length == 0 || char.IsWhiteSpace(entryText[^1]) ? entryText + text : $"{entryText} {text}";
        Dictation.Failed += message => Error = message;
    }

    private readonly IServerConnection connection;

    private readonly Func<DateOnly> today;

    private readonly TimeProvider time;

    private readonly TimeSpan autoSaveInterval = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim saving = new(1, 1);

    private readonly SynchronizationContext? context = SynchronizationContext.Current;

    private ITimer? autoSave;

    private readonly Dictionary<DateOnly, JournalEntry> entries = [];

    private DateOnly month;

    private DateOnly selected;

    private string entryText = string.Empty;

    private string savedText = string.Empty;

    private bool isCalendarOpen;

    private IReadOnlyList<CalendarDayViewModel> days = [];

    /// <summary>
    /// Gets the voice input of the entry, which adds what is said to the end of today's entry.
    /// </summary>
    public IDictation Dictation { get; }

    /// <summary>
    /// Gets the short weekday names above the calendar, Monday first.
    /// </summary>
    public IReadOnlyList<string> Weekdays { get; } = ["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"];

    /// <summary>
    /// Gets the cells of the calendar, six weeks starting on a Monday.
    /// </summary>
    public IReadOnlyList<CalendarDayViewModel> Days
    {
        get => days;
        private set => Set(ref days, value);
    }

    /// <summary>
    /// Gets a value indicating whether the calendar is open.
    /// </summary>
    public bool IsCalendarOpen
    {
        get => isCalendarOpen;
        private set
        {
            if (Set(ref isCalendarOpen, value))
            {
                Raise(nameof(IsEntryOpen));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the entry is shown, which is whenever the calendar is not: the screen shows one or the other.
    /// </summary>
    public bool IsEntryOpen => !isCalendarOpen;

    /// <summary>
    /// Gets the name of the month shown in the calendar, such as <c>October 2026</c>.
    /// </summary>
    public string MonthTitle => month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets the day shown.
    /// </summary>
    public DateOnly SelectedDate => selected;

    /// <summary>
    /// Gets the day shown, spelled out, such as <c>Friday, October 16</c>.
    /// </summary>
    public string SelectedTitle => selected.ToString("dddd, MMMM d", CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets or sets the text of the entry being written.
    /// </summary>
    public string EntryText
    {
        get => entryText;
        set
        {
            if (Set(ref entryText, value))
            {
                RaiseEntryState();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the shown day has an entry on the server.
    /// </summary>
    public bool HasEntry => entries.ContainsKey(selected);

    /// <summary>
    /// Gets a value indicating whether the text differs from what the server has for the day.
    /// </summary>
    public bool IsDirty => entryText != savedText;

    /// <summary>
    /// Gets a value indicating whether the shown day is today, the only day whose entry can be written.
    /// </summary>
    public bool CanEdit => selected == today();

    /// <summary>
    /// Gets a value indicating whether the shown entry is an earlier day's, which can only be read.
    /// </summary>
    public bool IsReadOnly => !CanEdit;

    /// <summary>
    /// Gets a value indicating whether today's entry has text the server does not have yet, which the automatic saving sends.
    /// </summary>
    public bool CanSave => CanEdit && IsDirty && !string.IsNullOrWhiteSpace(entryText);

    /// <summary>
    /// Gets the command that flips between the entry and the calendar.
    /// </summary>
    public AsyncCommand ToggleCalendarCommand { get; }

    /// <summary>
    /// Gets the command that shows the previous month.
    /// </summary>
    public AsyncCommand PreviousMonthCommand { get; }

    /// <summary>
    /// Gets the command that shows the next month.
    /// </summary>
    public AsyncCommand NextMonthCommand { get; }

    /// <summary>
    /// Selects today, shows the entry, reads the month around today and starts saving changed text every five seconds.
    /// </summary>
    /// <returns>A task that completes once the entries are read or the read failed.</returns>
    public async Task Load()
    {
        selected = today();
        month = new DateOnly(selected.Year, selected.Month, 1);
        IsCalendarOpen = false;
        autoSave ??= time.CreateTimer(_ => Post(() => _ = Flush()), null, autoSaveInterval, autoSaveInterval);
        await Refresh(true);
    }

    /// <summary>
    /// Sends today's text to the server now if it changed since it was last saved; does nothing otherwise.
    /// </summary>
    /// <returns>A task that completes once the text is saved or the save failed.</returns>
    public async Task Flush()
    {
        if (CanSave)
        {
            await Store();
        }
    }

    /// <summary>
    /// Stops the automatic saving and any recording; text that was not saved is not sent.
    /// </summary>
    public void Detach()
    {
        autoSave?.Dispose();
        autoSave = null;
        Dictation.Cancel();
    }

    /// <summary>
    /// Reacts to an event pushed by the server.
    /// </summary>
    /// <param name="remoteEvent">The event.</param>
    /// <returns>A task that completes once the screen is up to date.</returns>
    public async Task Handle(RemoteEvent remoteEvent)
    {
        if (remoteEvent.Kind == RemoteEventKind.EntryChanged)
        {
            await Refresh(remoteEvent.Date == selected && !IsDirty);
        }
    }

    private DateOnly GridStart() => month.AddDays(-(((int)month.DayOfWeek + 6) % 7));

    private bool IsSelectable(DateOnly date) => date == today() || (date < today() && entries.ContainsKey(date));

    private void RaiseEntryState()
    {
        Raise(nameof(IsDirty));
        Raise(nameof(CanSave));
        Raise(nameof(HasEntry));
    }

    private void SetText(string text)
    {
        savedText = text;
        entryText = text;
        Raise(nameof(EntryText));
        RaiseEntryState();
    }

    private void BuildDays()
    {
        DateOnly start = GridStart();
        DateOnly now = today();
        List<CalendarDayViewModel> cells = [];

        for (int index = 0; index < 42; index++)
        {
            DateOnly date = start.AddDays(index);
            bool selectable = IsSelectable(date);
            cells.Add(new CalendarDayViewModel(date, date.Month == month.Month, entries.ContainsKey(date), date == now, date == selected, selectable, new AsyncCommand(_ => Select(date), () => selectable)));
        }

        Days = cells;
        Raise(nameof(MonthTitle));
        Raise(nameof(SelectedTitle));
        Raise(nameof(SelectedDate));
        Raise(nameof(CanEdit));
        Raise(nameof(IsReadOnly));
        Dictation.Refresh();
    }

    private async Task Refresh(bool reloadText)
    {
        DateOnly start = GridStart();
        IReadOnlyList<JournalEntry> listed = [];

        if (!await Attempt(async () => listed = await connection.ListEntries(start, start.AddDays(41), CancellationToken.None)))
        {
            return;
        }

        entries.Clear();

        foreach (JournalEntry entry in listed)
        {
            entries[entry.Date] = entry;
        }

        BuildDays();

        if (reloadText)
        {
            SetText(entries.TryGetValue(selected, out JournalEntry? current) ? current.Text : string.Empty);
        }
        else
        {
            RaiseEntryState();
        }
    }

    private async Task ToggleCalendar()
    {
        if (IsCalendarOpen)
        {
            IsCalendarOpen = false;
            return;
        }

        month = new DateOnly(selected.Year, selected.Month, 1);
        IsCalendarOpen = true;
        await Refresh(false);
    }

    private async Task Select(DateOnly date)
    {
        if (!IsSelectable(date) || (CanSave && !await Store()))
        {
            return;
        }

        selected = date;
        month = new DateOnly(date.Year, date.Month, 1);
        IsCalendarOpen = false;
        await Refresh(true);
    }

    private async Task ChangeMonth(int offset)
    {
        month = month.AddMonths(offset);
        await Refresh(false);
    }

    private void Post(Action action)
    {
        if (context is null)
        {
            action();
        }
        else
        {
            context.Post(_ => action(), null);
        }
    }

    private async Task<bool> Store()
    {
        await saving.WaitAsync();

        try
        {
            if (!CanSave)
            {
                return true;
            }

            JournalEntry? saved = null;
            DateOnly date = selected;
            string text = entryText;

            if (!await Attempt(async () => saved = await connection.SaveEntry(date, text, CancellationToken.None)) || saved is null)
            {
                return false;
            }

            entries[date] = saved;

            if (selected == date && entryText == text)
            {
                savedText = saved.Text;
                entryText = saved.Text;
                Raise(nameof(EntryText));
            }

            BuildDays();
            RaiseEntryState();
            return true;
        }
        finally
        {
            saving.Release();
        }
    }
}
