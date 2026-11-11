namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// The journal screen: today's entry, written and saved here, and a list of the days that have an entry, newest first, to read an earlier day from.
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
        ToggleListCommand = new AsyncCommand(_ => ToggleList());
        Dictation = new Dictation(connection, recorder, () => CanEdit);
        Dictation.Dictated += text => EntryText = entryText.Length == 0 || char.IsWhiteSpace(entryText[^1]) ? entryText + text : $"{entryText} {text}";
        Dictation.Failed += message => Error = message;
    }

    private readonly IServerConnection connection;

    private readonly Func<DateOnly> today;

    private readonly TimeProvider time;

    private readonly TimeSpan autoSaveInterval = TimeSpan.FromSeconds(5);

    private readonly int pageSize = 50;

    private readonly SemaphoreSlim saving = new(1, 1);

    private readonly SynchronizationContext? context = SynchronizationContext.Current;

    private ITimer? autoSave;

    private DateOnly selected;

    private string entryText = string.Empty;

    private string savedText = string.Empty;

    private bool isListOpen;

    private bool hasMoreRows;

    private bool loadingMore;

    private int rowsVersion;

    /// <summary>
    /// Gets the voice input of the entry, which adds what is said to the end of today's entry.
    /// </summary>
    public IDictation Dictation { get; }

    /// <summary>
    /// Gets the rows of the list of entries, newest first: today's row, which is always there, and a row for every earlier day that has an entry that has been loaded so far.
    /// </summary>
    public ObservableCollection<EntryRowViewModel> Rows { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the list of entries is shown instead of the entry.
    /// </summary>
    public bool IsListOpen
    {
        get => isListOpen;
        private set
        {
            if (Set(ref isListOpen, value))
            {
                Raise(nameof(IsEntryOpen));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the entry is shown, which is whenever the list is not: the screen shows one or the other.
    /// </summary>
    public bool IsEntryOpen => !isListOpen;

    /// <summary>
    /// Gets a value indicating whether older days than the loaded rows are on the server.
    /// </summary>
    public bool HasMoreRows => hasMoreRows;

    /// <summary>
    /// Gets the day shown.
    /// </summary>
    public DateOnly SelectedDate => selected;

    /// <summary>
    /// Gets the day shown, spelled out, such as <c>Friday, October 16</c>. The space before the day does not break, so a date too long for a narrow screen wraps after the comma instead of leaving the day alone on the second line.
    /// </summary>
    public string SelectedTitle => selected.ToString("dddd, MMMM\u00A0d", CultureInfo.InvariantCulture);

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
    /// Gets the command that flips between the entry and the list of entries.
    /// </summary>
    public AsyncCommand ToggleListCommand { get; }

    /// <summary>
    /// Selects today, shows the entry and starts saving changed text every five seconds. Text typed and not yet sent is kept, so a connection that comes back never replaces it with what the server has.
    /// </summary>
    /// <returns>A task that completes once the entry is read or the read failed.</returns>
    public async Task Load()
    {
        selected = today();
        IsListOpen = false;
        autoSave ??= time.CreateTimer(_ => Post(() => _ = Flush()), null, autoSaveInterval, autoSaveInterval);
        RaiseSelection();
        await ReloadSelected();
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
    /// Loads the next page of older days into the list, when there is one and none is being loaded.
    /// </summary>
    /// <returns>A task that completes once the page is added or the read failed.</returns>
    public async Task LoadMore()
    {
        if (!hasMoreRows || loadingMore || Rows.Count == 0)
        {
            return;
        }

        loadingMore = true;
        int version = rowsVersion;

        try
        {
            IReadOnlyList<EntryDigest> page = [];
            DateOnly before = Rows[^1].Date;

            if (!await Attempt(async () => page = await connection.ListDigests(before, pageSize, CancellationToken.None)) || version != rowsVersion)
            {
                return;
            }

            hasMoreRows = page.Count >= pageSize;
            Raise(nameof(HasMoreRows));

            foreach (EntryDigest digest in page.Where(digest => digest.Date < before))
            {
                Rows.Add(NewRow(digest.Date, digest));
            }
        }
        finally
        {
            loadingMore = false;
        }
    }

    /// <summary>
    /// Reacts to an event pushed by the server.
    /// </summary>
    /// <param name="remoteEvent">The event.</param>
    /// <returns>A task that completes once the screen is up to date.</returns>
    public async Task Handle(RemoteEvent remoteEvent)
    {
        if (remoteEvent is not { Kind: RemoteEventKind.EntryChanged, Date: { } date })
        {
            return;
        }

        if (IsListOpen)
        {
            ApplyToRows(date, remoteEvent.Digest);
        }

        if (date == selected && !IsDirty)
        {
            await ReloadSelected();
        }
    }

    private EntryRowViewModel NewRow(DateOnly date, EntryDigest? digest)
    {
        EntryRowViewModel row = new(date, today(), date == selected, new AsyncCommand(_ => Select(date)));
        row.Update(digest);
        return row;
    }

    private void RaiseEntryState()
    {
        Raise(nameof(IsDirty));
        Raise(nameof(CanSave));
    }

    private void RaiseSelection()
    {
        Raise(nameof(SelectedDate));
        Raise(nameof(SelectedTitle));
        Raise(nameof(CanEdit));
        Raise(nameof(IsReadOnly));
        RaiseEntryState();

        foreach (EntryRowViewModel row in Rows)
        {
            row.IsSelected = row.Date == selected;
        }

        Dictation.Refresh();
    }

    private void SetText(string text)
    {
        savedText = text;
        entryText = text;
        Raise(nameof(EntryText));
        RaiseEntryState();
    }

    private async Task ReloadSelected()
    {
        DateOnly date = selected;
        JournalEntry? entry = null;

        if (!await Attempt(async () => entry = await connection.GetEntry(date, CancellationToken.None)) || selected != date || IsDirty)
        {
            return;
        }

        SetText(entry?.Text ?? string.Empty);
    }

    private async Task ReloadRows()
    {
        IReadOnlyList<EntryDigest> page = [];

        if (!await Attempt(async () => page = await connection.ListDigests(null, pageSize, CancellationToken.None)))
        {
            return;
        }

        rowsVersion++;
        DateOnly now = today();
        hasMoreRows = page.Count >= pageSize;
        Raise(nameof(HasMoreRows));
        Rows.Clear();

        foreach (EntryDigest digest in page.Where(digest => digest.Date <= now))
        {
            Rows.Add(NewRow(digest.Date, digest));
        }

        if (Rows.Count == 0 || Rows[0].Date != now)
        {
            Rows.Insert(0, NewRow(now, null));
        }
    }

    private void ApplyToRows(DateOnly date, EntryDigest? digest)
    {
        if (date > today())
        {
            return;
        }

        EntryRowViewModel? row = Rows.FirstOrDefault(existing => existing.Date == date);

        if (row is not null)
        {
            if (digest is null && !row.IsToday)
            {
                Rows.Remove(row);
            }
            else
            {
                row.Update(digest);
            }

            return;
        }

        if (digest is null || (hasMoreRows && Rows.Count > 0 && date < Rows[^1].Date))
        {
            return;
        }

        Rows.Insert(Rows.TakeWhile(existing => existing.Date > date).Count(), NewRow(date, digest));
    }

    private async Task ToggleList()
    {
        if (IsListOpen)
        {
            IsListOpen = false;
            return;
        }

        IsListOpen = true;
        await Flush();
        await ReloadRows();
    }

    private async Task Select(DateOnly date)
    {
        if (date > today() || (CanSave && !await Store()))
        {
            return;
        }

        if (date != selected)
        {
            JournalEntry? entry = null;

            if (!await Attempt(async () => entry = await connection.GetEntry(date, CancellationToken.None)))
            {
                return;
            }

            selected = date;
            SetText(entry?.Text ?? string.Empty);
        }

        IsListOpen = false;
        RaiseSelection();
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

            if (selected == date && entryText == text)
            {
                savedText = saved.Text;
                entryText = saved.Text;
                Raise(nameof(EntryText));
            }

            RaiseEntryState();
            return true;
        }
        finally
        {
            saving.Release();
        }
    }
}
