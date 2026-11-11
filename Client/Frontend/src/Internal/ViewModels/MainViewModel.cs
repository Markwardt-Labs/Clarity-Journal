namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// The root of the app: shows the connect screen until a server connection is open, then the session, and the updating screen in place of either while an update runs.
/// </summary>
/// <param name="info">The product name and version.</param>
/// <param name="factory">Opens connections.</param>
/// <param name="store">Remembers the server between runs.</param>
/// <param name="recorder">Records speech for voice input, or <see langword="null" /> when this client cannot record, which hides the voice input buttons.</param>
/// <param name="feed">Finds the newest release on GitHub, or <see langword="null" /> when this client does not look for updates.</param>
/// <param name="updater">Installs a release over this app, or <see langword="null" /> when this app cannot update itself, in which case an update only replaces the server.</param>
/// <param name="retryInterval">How long to wait between attempts to reach a saved server that could not be reached, or <see langword="null" /> for three seconds.</param>
internal sealed class MainViewModel(IAppInfo info, IServerConnection.IFactory factory, ISettingsStore store, IVoiceRecorder? recorder = null, IReleaseFeed? feed = null, IClientUpdater? updater = null, TimeSpan? retryInterval = null) : ViewModelBase
{
    private readonly TimeSpan retryEvery = retryInterval ?? TimeSpan.FromSeconds(3);

    private readonly TimeSpan retryFor = TimeSpan.FromMinutes(2);

    private IServerConnection? connection;

    private SessionViewModel? session;

    private UpdateViewModel? update;

    private object? beforeUpdate;

    private object? current;

    /// <summary>
    /// Gets the screen being shown: a <see cref="ConnectViewModel" />, a <see cref="SessionViewModel" /> or, while an update runs, an <see cref="UpdateViewModel" />.
    /// </summary>
    public object? Current
    {
        get => current;
        private set => Set(ref current, value);
    }

    /// <summary>
    /// Connects to the saved server, or shows the connect screen if there is none or it cannot be reached. A saved server that cannot be reached is tried again every few seconds for two minutes, because an app that restarted after its update usually starts before its server is back.
    /// </summary>
    /// <returns>A task that completes once a screen is showing.</returns>
    public async Task Start()
    {
        ConnectionSettings? saved = await store.Load(CancellationToken.None);

        if (saved is null)
        {
            ShowConnect(null, null, false);
            return;
        }

        try
        {
            await Open(await factory.Connect(saved, CancellationToken.None), saved);
        }
        catch (CertificateChangedException exception)
        {
            ShowConnect(saved, exception.Message, true);
        }
        catch (Exception exception) when (exception is IOException or SocketException or UnauthorizedAccessException)
        {
            ConnectViewModel connect = ShowConnect(saved, exception.Message, false);

            if (exception is not UnauthorizedAccessException)
            {
                _ = Retry(connect, saved);
            }
        }
    }

    /// <summary>
    /// Reopens the connection and reads everything again, for when the app returns from the background and the connection may have died silently.
    /// </summary>
    /// <returns>A task that completes once the connection is back or the attempt failed.</returns>
    public async Task Resume()
    {
        if (connection is not { } open)
        {
            return;
        }

        try
        {
            await open.Reconnect(CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or SocketException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Sends today's journal text to the server now if it was not saved yet, for when the app leaves the screen and may be stopped before the next automatic save.
    /// </summary>
    /// <returns>A task that completes once the text is saved or the save failed.</returns>
    public Task Flush()
        => session?.Journal.Flush() ?? Task.CompletedTask;

    private async Task Prepare()
    {
        await Flush();

        if (session?.Journal.CanSave == true)
        {
            throw new InvalidOperationException("Today's entry could not be saved, so the update did not start.");
        }
    }

    private async Task Retry(ConnectViewModel connect, ConnectionSettings saved)
    {
        DateTime end = DateTime.UtcNow + retryFor;

        while (DateTime.UtcNow < end)
        {
            await Task.Delay(retryEvery);

            if (!ReferenceEquals(current, connect) || connect.IsBusy || connect.Host != saved.Host || connect.Port != saved.Port.ToString(CultureInfo.InvariantCulture) || connect.AccessKey != saved.AccessKey)
            {
                return;
            }

            IServerConnection opened;

            try
            {
                opened = await factory.Connect(saved, CancellationToken.None);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or CertificateChangedException)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or SocketException)
            {
                continue;
            }

            if (ReferenceEquals(current, connect) && !connect.IsBusy)
            {
                await Open(opened, saved);
            }
            else
            {
                await opened.DisposeAsync();
            }

            return;
        }
    }

    private ConnectViewModel ShowConnect(ConnectionSettings? settings, string? error, bool certificateChanged)
    {
        ConnectViewModel connect = new(factory, settings, $"{info.Name} {info.Version}");
        connect.Connected += async (opened, used) =>
        {
            await store.Save(used, CancellationToken.None);
            await Open(opened, used);
        };

        if (error is not null)
        {
            connect.Fail(error, certificateChanged);
        }

        Current = connect;
        return connect;
    }

    private async Task Open(IServerConnection opened, ConnectionSettings used)
    {
        await Close();
        connection = opened;
        UpdateViewModel updating = update = Track(opened);
        SessionViewModel opening = new(opened, used, $"{info.Name} {info.Version}", null, recorder, null, updating);
        session = opening;
        opening.Disconnected += async () =>
        {
            ConnectionSettings? saved = await store.Load(CancellationToken.None);
            await Close();
            ShowConnect(saved, null, false);
        };
        Current = opening;
        await opening.Start();
    }

    private UpdateViewModel Track(IServerConnection opened)
    {
        UpdateViewModel tracking = new(info, opened, feed, updater, Prepare);
        tracking.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(UpdateViewModel.Phase))
            {
                ShowUpdate(tracking);
            }
        };
        _ = tracking.Watch();
        return tracking;
    }

    private void ShowUpdate(UpdateViewModel updating)
    {
        if (updating.IsUpdating && current is not UpdateViewModel)
        {
            beforeUpdate = current;
            Current = updating;
        }
        else if (!updating.IsUpdating && current is UpdateViewModel)
        {
            Current = beforeUpdate;
            beforeUpdate = null;
        }
    }

    private async Task Close()
    {
        session?.Detach();
        session = null;
        beforeUpdate = null;
        update?.Dispose();
        update = null;

        if (connection is { } open)
        {
            connection = null;
            await open.DisposeAsync();
        }
    }
}
