namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// The app while connected: the bottom tabs and the screens behind them.
/// </summary>
internal sealed class SessionViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SessionViewModel" /> class and listens to the connection.
    /// </summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="settings">The settings the connection was opened with.</param>
    /// <param name="version">The product name and version to show.</param>
    /// <param name="today">Gives the current day on the device, or <see langword="null" /> for the system clock.</param>
    /// <param name="recorder">Records speech for voice input, or <see langword="null" /> when this client cannot record.</param>
    /// <param name="time">Times the journal's automatic saving, or <see langword="null" /> for the system clock.</param>
    public SessionViewModel(IServerConnection connection, ConnectionSettings settings, string version, Func<DateOnly>? today = null, IVoiceRecorder? recorder = null, TimeProvider? time = null)
    {
        this.connection = connection;
        Server = $"{settings.Host}:{settings.Port}";
        Version = version;
        Journal = new JournalViewModel(connection, today ?? (() => DateOnly.FromDateTime(DateTime.Now)), recorder, time);
        Summaries = new SummariesViewModel(connection);
        Chat = new ChatViewModel(connection, recorder);
        SelectTabCommand = new RelayCommand(tab => SelectedTab = tab is AppTab selected ? selected : AppTab.Journal);
        DisconnectCommand = new RelayCommand(_ => Disconnected?.Invoke());
        connection.EventReceived += OnEvent;
        connection.Lost += OnLost;
        connection.Reconnected += OnReconnected;
    }

    private readonly IServerConnection connection;

    private AppTab selectedTab;

    private bool isConnected = true;

    /// <summary>
    /// Raised when the user chooses to disconnect from the server.
    /// </summary>
    public event Action? Disconnected;

    /// <summary>
    /// Gets the host and port of the server.
    /// </summary>
    public string Server { get; }

    /// <summary>
    /// Gets the product name and version.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets the journal screen.
    /// </summary>
    public JournalViewModel Journal { get; }

    /// <summary>
    /// Gets the summaries screen.
    /// </summary>
    public SummariesViewModel Summaries { get; }

    /// <summary>
    /// Gets the chat screen.
    /// </summary>
    public ChatViewModel Chat { get; }

    /// <summary>
    /// Gets or sets the tab being shown.
    /// </summary>
    public AppTab SelectedTab
    {
        get => selectedTab;
        set
        {
            AppTab previous = selectedTab;

            if (Set(ref selectedTab, value))
            {
                StopDictation(previous);
                Raise(nameof(IsJournal));
                Raise(nameof(IsSummaries));
                Raise(nameof(IsChat));
                Raise(nameof(IsSettings));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the journal tab is shown.
    /// </summary>
    public bool IsJournal => selectedTab == AppTab.Journal;

    /// <summary>
    /// Gets a value indicating whether the summaries tab is shown.
    /// </summary>
    public bool IsSummaries => selectedTab == AppTab.Summaries;

    /// <summary>
    /// Gets a value indicating whether the chat tab is shown.
    /// </summary>
    public bool IsChat => selectedTab == AppTab.Chat;

    /// <summary>
    /// Gets a value indicating whether the settings tab is shown.
    /// </summary>
    public bool IsSettings => selectedTab == AppTab.Settings;

    /// <summary>
    /// Gets a value indicating whether the connection to the server is up.
    /// </summary>
    public bool IsConnected
    {
        get => isConnected;
        private set
        {
            if (Set(ref isConnected, value))
            {
                Raise(nameof(IsDisconnected));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the connection to the server is down and being reopened.
    /// </summary>
    public bool IsDisconnected => !isConnected;

    /// <summary>
    /// Gets the command that shows a tab; its parameter is the <see cref="AppTab" />.
    /// </summary>
    public RelayCommand SelectTabCommand { get; }

    /// <summary>
    /// Gets the command that leaves the server and returns to the connect screen.
    /// </summary>
    public RelayCommand DisconnectCommand { get; }

    /// <summary>
    /// Reads everything the screens show.
    /// </summary>
    /// <returns>A task that completes once every screen has loaded or failed.</returns>
    public async Task Start()
    {
        await Journal.Load();
        await Summaries.Load();
        await Chat.Load();
    }

    /// <summary>
    /// Stops listening to the connection.
    /// </summary>
    public void Detach()
    {
        Journal.Detach();
        Chat.Dictation.Cancel();
        connection.EventReceived -= OnEvent;
        connection.Lost -= OnLost;
        connection.Reconnected -= OnReconnected;
    }

    private void StopDictation(AppTab left)
    {
        if (left == AppTab.Journal)
        {
            _ = Journal.Dictation.Stop();
            _ = Journal.Flush();
        }
        else if (left == AppTab.Chat)
        {
            _ = Chat.Dictation.Stop();
        }
    }

    private void OnLost() => IsConnected = false;

    private async void OnReconnected()
    {
        IsConnected = true;
        await Start();
    }

    private async void OnEvent(RemoteEvent remoteEvent)
    {
        await Journal.Handle(remoteEvent);
        await Summaries.Handle(remoteEvent);
        await Chat.Handle(remoteEvent);
    }
}
