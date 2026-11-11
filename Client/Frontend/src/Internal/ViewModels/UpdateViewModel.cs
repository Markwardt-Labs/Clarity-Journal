namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// Looks on GitHub for a release newer than this app and its server and carries out the update the user confirms: the server first, which restarts, then the app.
/// </summary>
internal sealed class UpdateViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateViewModel" /> class.
    /// </summary>
    /// <param name="info">The version of this app.</param>
    /// <param name="connection">The connection to the server, whose version is compared and which is told to update.</param>
    /// <param name="feed">Finds the newest release, or <see langword="null" /> when the app does not look for one.</param>
    /// <param name="updater">Installs the release over this app, or <see langword="null" /> when this app cannot update itself and only updates its server.</param>
    /// <param name="prepare">Runs before anything is restarted or replaced, such as saving what was typed; a failure, reported as an <see cref="InvalidOperationException" />, stops the update. <see langword="null" /> to do nothing.</param>
    /// <param name="interval">How often to look again, or <see langword="null" /> for every hour.</param>
    public UpdateViewModel(IAppInfo info, IServerConnection connection, IReleaseFeed? feed, IClientUpdater? updater, Func<Task>? prepare = null, TimeSpan? interval = null)
    {
        this.info = info;
        this.connection = connection;
        this.feed = feed;
        this.updater = updater;
        this.prepare = prepare;
        this.interval = interval ?? TimeSpan.FromHours(1);
        BeginCommand = new AsyncCommand(_ =>
        {
            Begin();
            return Task.CompletedTask;
        });
        ConfirmCommand = new AsyncCommand(_ => Run());
        CloseCommand = new AsyncCommand(_ =>
        {
            Close();
            return Task.CompletedTask;
        });
        connection.Reconnected += Reconnected;
    }

    private readonly IAppInfo info;

    private readonly IServerConnection connection;

    private readonly IReleaseFeed? feed;

    private readonly IClientUpdater? updater;

    private readonly Func<Task>? prepare;

    private readonly TimeSpan interval;

    private readonly CancellationTokenSource stopping = new();

    private ReleaseInfo? latest;

    private UpdatePhase phase;

    private string status = string.Empty;

    private double progress;

    /// <summary>
    /// Gets the command that asks for the confirmation of the update.
    /// </summary>
    public AsyncCommand BeginCommand { get; }

    /// <summary>
    /// Gets the command that carries out the update.
    /// </summary>
    public AsyncCommand ConfirmCommand { get; }

    /// <summary>
    /// Gets the command that closes the confirmation or the updating screen.
    /// </summary>
    public AsyncCommand CloseCommand { get; }

    /// <summary>
    /// Gets how far the update has come.
    /// </summary>
    public UpdatePhase Phase
    {
        get => phase;
        private set
        {
            if (Set(ref phase, value))
            {
                Raise(nameof(IsOpen));
                Raise(nameof(IsUpdating));
                Raise(nameof(IsWorking));
                Raise(nameof(IsInstalling));
                Raise(nameof(CanClose));
                Raise(nameof(CloseText));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the confirmation dialog is showing.
    /// </summary>
    public bool IsOpen => phase == UpdatePhase.Confirming;

    /// <summary>
    /// Gets a value indicating whether the updating screen is showing: from the confirmation on, the update runs on that screen and in the background of the app.
    /// </summary>
    public bool IsUpdating => phase is UpdatePhase.Working or UpdatePhase.Waiting or UpdatePhase.Finished;

    /// <summary>
    /// Gets a value indicating whether the update is running or waiting for the server to restart, which shows the progress bar.
    /// </summary>
    public bool IsWorking => phase is UpdatePhase.Working or UpdatePhase.Waiting;

    /// <summary>
    /// Gets a value indicating whether the update is running and cannot be interrupted.
    /// </summary>
    public bool IsInstalling => phase == UpdatePhase.Working;

    /// <summary>
    /// Gets a value indicating whether the confirmation or the updating screen may be left, which the updating screen may not while the update is running.
    /// </summary>
    public bool CanClose => phase != UpdatePhase.Working;

    /// <summary>
    /// Gets the label of the button that closes the confirmation or the updating screen.
    /// </summary>
    public string CloseText => phase == UpdatePhase.Confirming ? "Cancel" : "Close";

    /// <summary>
    /// Gets what the update is doing now.
    /// </summary>
    public string Status
    {
        get => status;
        private set => Set(ref status, value);
    }

    /// <summary>
    /// Gets the fraction of the download that is done, from 0 to 1.
    /// </summary>
    public double Progress
    {
        get => progress;
        private set
        {
            if (Set(ref progress, value))
            {
                Raise(nameof(IsProgressUnknown));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether nothing was downloaded yet, so that the progress bar only shows that something is going on.
    /// </summary>
    public bool IsProgressUnknown => progress <= 0;

    /// <summary>
    /// Gets the newest release found on GitHub, or <see langword="null" /> when none was found yet.
    /// </summary>
    public ReleaseInfo? Latest => latest;

    /// <summary>
    /// Gets a value indicating whether this app has a version older than the newest release and can install it.
    /// </summary>
    public bool ClientNeedsUpdate => updater is { IsSupported: true } && latest is not null && AppVersion.TryParse(info.Version, out AppVersion? own) && latest.Version > own;

    /// <summary>
    /// Gets a value indicating whether the server has a version older than the newest release, or does not say which it has.
    /// </summary>
    public bool ServerNeedsUpdate => latest is not null && (!AppVersion.TryParse(connection.ServerVersion, out AppVersion? server) || latest.Version > server);

    /// <summary>
    /// Gets a value indicating whether a newer release exists for the app or its server, which shows the update option.
    /// </summary>
    public bool IsAvailable => ClientNeedsUpdate || ServerNeedsUpdate;

    /// <summary>
    /// Gets the label of the option that starts the update.
    /// </summary>
    public string ButtonText => latest is null ? "Update" : $"Update to {latest.Version}";

    /// <summary>
    /// Gets what the update replaces, for the confirmation.
    /// </summary>
    public string Summary => (ClientNeedsUpdate, ServerNeedsUpdate) switch
    {
        (true, true) => $"Version {latest!.Version} replaces {info.Version} in this app and {connection.ServerVersion ?? "the older version"} on the server.",
        (true, false) => $"Version {latest!.Version} replaces {info.Version} in this app.",
        (false, true) => $"Version {latest!.Version} replaces {connection.ServerVersion ?? "the older version"} on the server.",
        _ => "The app and its server are up to date.",
    };

    /// <summary>
    /// Gets what the confirmation warns about, which is the server's restart; empty when only this app updates.
    /// </summary>
    public string Warning => ServerNeedsUpdate ? "The server restarts for a moment. A summary or an answer that it is writing is interrupted." : string.Empty;

    /// <summary>
    /// Gets a value indicating whether <see cref="Warning" /> has something to say.
    /// </summary>
    public bool HasWarning => Warning.Length > 0;

    /// <summary>
    /// Looks up the newest release, then keeps looking at intervals until the view model is disposed.
    /// </summary>
    /// <returns>A task that ends when the view model is disposed.</returns>
    public async Task Watch()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                await Check();
                await Task.Delay(interval, stopping.Token);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Looks up the newest release once. A lookup that fails, such as without a connection to the internet, leaves what was found before and is tried again at the next interval.
    /// </summary>
    /// <returns>A task that completes when the answer is in.</returns>
    public async Task Check()
    {
        if (feed is null)
        {
            return;
        }

        try
        {
            latest = await feed.Latest(stopping.Token);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            return;
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }

        Refresh();
    }

    /// <summary>
    /// Shows the confirmation when an update is available.
    /// </summary>
    public void Begin()
    {
        if (phase == UpdatePhase.Idle && IsAvailable)
        {
            Error = null;
            Status = string.Empty;
            Progress = 0;
            Raise(nameof(Warning));
            Raise(nameof(HasWarning));
            Phase = UpdatePhase.Confirming;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        connection.Reconnected -= Reconnected;
        stopping.Cancel();
        stopping.Dispose();
    }

    private void Close()
    {
        if (CanClose)
        {
            Phase = UpdatePhase.Idle;
        }
    }

    private void Refresh()
    {
        Raise(nameof(Latest));
        Raise(nameof(ClientNeedsUpdate));
        Raise(nameof(ServerNeedsUpdate));
        Raise(nameof(IsAvailable));
        Raise(nameof(ButtonText));
        Raise(nameof(Summary));
        Raise(nameof(Warning));
        Raise(nameof(HasWarning));
    }

    private void Reconnected()
    {
        if (phase == UpdatePhase.Waiting)
        {
            Phase = UpdatePhase.Idle;
        }

        Refresh();
        _ = Check();
    }

    private async Task Run()
    {
        if (phase != UpdatePhase.Confirming || latest is not { } release)
        {
            return;
        }

        bool server = ServerNeedsUpdate;
        bool client = ClientNeedsUpdate;
        Error = null;
        Phase = UpdatePhase.Working;

        try
        {
            if (prepare is not null)
            {
                Status = "Saving your entry";
                await prepare();
            }

            if (server)
            {
                Status = "Updating the server";

                try
                {
                    await connection.UpdateServer(CancellationToken.None);
                }
                catch (Exception exception) when (connection.ServerVersion is null && exception is IOException or RemoteException)
                {
                    throw new InvalidOperationException($"The server is too old to update itself ({exception.Message}). Install its newest release by hand.");
                }
            }

            if (client && updater is not null)
            {
                Status = $"Downloading version {release.Version}";
                await updater.Install(release, new Progress<double>(fraction => Progress = fraction), CancellationToken.None);
                Status = "The installer of the system finishes the update.";
                Phase = UpdatePhase.Finished;
            }
            else
            {
                Status = "The server restarts. This closes once it is back.";
                Phase = UpdatePhase.Waiting;
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException or RemoteException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            Error = exception.Message;
            Phase = UpdatePhase.Confirming;
        }
    }
}
