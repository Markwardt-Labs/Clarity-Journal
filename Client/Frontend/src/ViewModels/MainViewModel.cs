namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// The root of the app: shows the connect screen until a server connection is open, then the session.
/// </summary>
/// <param name="info">The product name and version.</param>
/// <param name="factory">Opens connections.</param>
/// <param name="store">Remembers the server between runs.</param>
/// <param name="recorder">Records speech for voice input, or <see langword="null" /> when this client cannot record, which hides the voice input buttons.</param>
internal sealed class MainViewModel(IAppInfo info, IServerConnection.IFactory factory, ISettingsStore store, IVoiceRecorder? recorder = null) : ViewModelBase
{
    private IServerConnection? connection;

    private SessionViewModel? session;

    private object? current;

    /// <summary>
    /// Gets the screen being shown: a <see cref="ConnectViewModel" /> or a <see cref="SessionViewModel" />.
    /// </summary>
    public object? Current
    {
        get => current;
        private set => Set(ref current, value);
    }

    /// <summary>
    /// Connects to the saved server, or shows the connect screen if there is none or it cannot be reached.
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
            ShowConnect(saved, exception.Message, false);
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
    public Task Flush() => session?.Journal.Flush() ?? Task.CompletedTask;

    private void ShowConnect(ConnectionSettings? settings, string? error, bool certificateChanged)
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
    }

    private async Task Open(IServerConnection opened, ConnectionSettings used)
    {
        await Close();
        connection = opened;
        SessionViewModel opening = new(opened, used, $"{info.Name} {info.Version}", null, recorder);
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

    private async Task Close()
    {
        session?.Detach();
        session = null;

        if (connection is { } open)
        {
            connection = null;
            await open.DisposeAsync();
        }
    }
}
