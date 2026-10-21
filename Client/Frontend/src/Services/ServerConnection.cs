namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// An authenticated connection to the server. Requests may overlap; each call completes when its own response arrives.
/// </summary>
internal interface IServerConnection : IAsyncDisposable
{
    /// <summary>
    /// Raised when the server pushes an event; on the thread the connection was opened on when it has a synchronization context.
    /// </summary>
    event Action<RemoteEvent>? EventReceived;

    /// <summary>
    /// Raised when the connection to the server is lost. It keeps trying to reopen by itself, so <see cref="Reconnected" /> follows once it succeeds.
    /// </summary>
    event Action? Lost;

    /// <summary>
    /// Raised once the connection was reopened after it was lost, which means events were missed meanwhile and whatever follows them must be read again; on the thread the connection was opened on when it has a synchronization context.
    /// </summary>
    event Action? Reconnected;

    /// <summary>
    /// Gets the SHA-256 fingerprint of the self-signed certificate the server presented when the connection was opened, which later connections must see again; empty when a public authority vouches for the certificate, which is then not pinned.
    /// </summary>
    string Fingerprint { get; }

    /// <summary>
    /// Drops the connection and opens a new one, for a connection that may have died silently, such as when the app was in the background; <see cref="Reconnected" /> is raised once it is open.
    /// </summary>
    /// <param name="cancellation">Cancels the wait.</param>
    /// <exception cref="IOException">The server cannot be reached.</exception>
    Task Reconnect(CancellationToken cancellation);

    /// <summary>
    /// Lists the entries between two days.
    /// </summary>
    /// <param name="from">The first day, or <see langword="null" /> for no lower bound.</param>
    /// <param name="to">The last day, or <see langword="null" /> for no upper bound.</param>
    /// <param name="cancellation">Cancels the wait.</param>
    /// <returns>The entries, oldest first.</returns>
    Task<IReadOnlyList<JournalEntry>> ListEntries(DateOnly? from, DateOnly? to, CancellationToken cancellation);

    /// <summary>
    /// Reads the entry of a day.
    /// </summary>
    /// <param name="date">The day.</param>
    /// <param name="cancellation">Cancels the wait.</param>
    /// <returns>The entry, or <see langword="null" /> if the day has none.</returns>
    Task<JournalEntry?> GetEntry(DateOnly date, CancellationToken cancellation);

    /// <summary>
    /// Writes the entry of a day, replacing the one it has.
    /// </summary>
    /// <param name="date">The day.</param>
    /// <param name="text">The entry text.</param>
    /// <param name="cancellation">Cancels the wait.</param>
    /// <returns>The stored entry.</returns>
    /// <exception cref="RemoteException">The server refused the entry.</exception>
    Task<JournalEntry> SaveEntry(DateOnly date, string text, CancellationToken cancellation);

    /// <summary>
    /// Deletes the entry of a day.
    /// </summary>
    /// <param name="date">The day.</param>
    /// <param name="cancellation">Cancels the wait.</param>
    /// <exception cref="RemoteException">The day has no entry.</exception>
    Task DeleteEntry(DateOnly date, CancellationToken cancellation);

    /// <summary>
    /// Lists the summaries of a kind.
    /// </summary>
    /// <param name="kind">The kind of summary.</param>
    /// <param name="cancellation">Cancels the wait.</param>
    /// <returns>The summaries, newest period first.</returns>
    Task<IReadOnlyList<Summary>> GetSummaries(SummaryKind kind, CancellationToken cancellation);

    /// <summary>
    /// Reads the chat.
    /// </summary>
    /// <param name="cancellation">Cancels the wait.</param>
    /// <returns>The messages and whether an answer is being written.</returns>
    Task<ChatHistory> GetChat(CancellationToken cancellation);

    /// <summary>
    /// Sends a question to Claude Code; the answer arrives as an event.
    /// </summary>
    /// <param name="text">The question.</param>
    /// <param name="cancellation">Cancels the wait.</param>
    Task SendChat(string text, CancellationToken cancellation);

    /// <summary>
    /// Deletes the chat and starts a fresh conversation.
    /// </summary>
    /// <param name="cancellation">Cancels the wait.</param>
    Task ClearChat(CancellationToken cancellation);

    /// <summary>
    /// Turns a recording of speech into text on the server.
    /// </summary>
    /// <param name="audio">The recording as a WAV file: 16 kHz, mono, 16 bit.</param>
    /// <param name="cancellation">Cancels the wait.</param>
    /// <returns>The text, empty when nothing was heard.</returns>
    /// <exception cref="RemoteException">Voice input is turned off on the server, the recording is invalid or the transcription failed.</exception>
    Task<string> Transcribe(ReadOnlyMemory<byte> audio, CancellationToken cancellation);

    /// <summary>
    /// Creates <see cref="IServerConnection" /> instances.
    /// </summary>
    public interface IFactory
    {
        /// <summary>
        /// Connects to the server and proves the access key.
        /// </summary>
        /// <param name="settings">Where the server is and the key it requires; a fingerprint in it must match the server's certificate unless a public authority vouches for it.</param>
        /// <param name="cancellation">Cancels the connect.</param>
        /// <returns>The open connection.</returns>
        /// <exception cref="IOException">The server cannot be reached or closed the connection.</exception>
        /// <exception cref="CertificateChangedException">The server's certificate is not the one the settings trust.</exception>
        /// <exception cref="UnauthorizedAccessException">The server rejected the access key.</exception>
        Task<IServerConnection> Connect(ConnectionSettings settings, CancellationToken cancellation);
    }

    /// <inheritdoc />
    public sealed class Factory(IMessageChannel.IFactory channels, ILog log) : IFactory
    {
        private readonly TimeSpan timeout = TimeSpan.FromSeconds(10);

        private readonly ICertificateTrust trust = new CertificateTrust();

        /// <inheritdoc />
        public async Task<IServerConnection> Connect(ConnectionSettings settings, CancellationToken cancellation)
        {
            (ClientWebSocket socket, IMessageChannel channel, string fingerprint) = await Open(settings, cancellation);
            ConnectionSettings pinned = settings with { Fingerprint = fingerprint };
            return new ServerConnection(socket, channel, fingerprint, async token =>
            {
                (ClientWebSocket opened, IMessageChannel openedChannel, _) = await Open(pinned, token);
                return (opened, openedChannel);
            }, log, $"{settings.Host}:{settings.Port}");
        }

        private async Task<(ClientWebSocket socket, IMessageChannel channel, string fingerprint)> Open(ConnectionSettings settings, CancellationToken cancellation)
        {
            ClientWebSocket socket = new();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(20);
            string server = $"{settings.Host}:{settings.Port}";
            string stage = "connecting";

            try
            {
                using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                limit.CancelAfter(timeout);
                string fingerprint = string.Empty;
                bool changed = false;
                socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                {
                    if (certificate is null)
                    {
                        return false;
                    }

                    string presented = Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
                    fingerprint = trust.Pin(presented, errors);
                    changed = !trust.Accept(settings.Fingerprint, presented, errors);
                    return !changed;
                };

                Uri address = new UriBuilder("wss", settings.Host, settings.Port).Uri;
                log.Write($"Connecting to {address}");

                try
                {
                    await socket.ConnectAsync(address, limit.Token);
                }
                catch (WebSocketException exception)
                {
                    throw changed
                        ? new CertificateChangedException($"The certificate of {server} is not the one you first trusted. If you replaced the server's certificate, trust the new one.")
                        : new IOException($"Could not connect to {server}: {exception.InnerException?.Message ?? exception.Message}", exception);
                }

                log.Write($"WebSocket and TLS established with {server} ({(fingerprint.Length == 0 ? "certificate trusted by a public authority" : $"self-signed certificate SHA-256 {fingerprint}")})");
                stage = "the key handshake";
                IMessageChannel channel = channels.Create(new WebSocketLineStream(socket));
                await channel.Send(new HandshakeRequest { AccessKey = settings.AccessKey }, limit.Token);
                HandshakeResponse? reply = await channel.Receive<HandshakeResponse>(limit.Token);

                if (reply is null)
                {
                    throw new IOException("The server closed the connection.");
                }

                if (!reply.Accepted)
                {
                    throw new UnauthorizedAccessException("The server rejected the access key.");
                }

                log.Write($"Connected to {server}");
                return (socket, channel, fingerprint);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                log.Write($"Connection to {server} failed during {stage}: timed out after {timeout.TotalSeconds:0} seconds");
                socket.Dispose();
                throw new IOException("Timed out waiting for the server.");
            }
            catch (Exception exception)
            {
                log.Write($"Connection to {server} failed during {stage}: {exception.GetType().Name}: {exception.Message}");
                socket.Dispose();
                throw;
            }
        }
    }
}

/// <inheritdoc />
internal sealed class ServerConnection : IServerConnection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ServerConnection" /> class and starts reading responses.
    /// </summary>
    /// <param name="socket">The open socket.</param>
    /// <param name="channel">The message channel over the socket.</param>
    /// <param name="fingerprint">The fingerprint of the certificate the server presented.</param>
    /// <param name="reopen">Opens a new socket and channel after the connection was lost.</param>
    /// <param name="log">Receives diagnostic lines.</param>
    /// <param name="server">The server's address, for log lines.</param>
    public ServerConnection(ClientWebSocket socket, IMessageChannel channel, string fingerprint, Func<CancellationToken, Task<(ClientWebSocket socket, IMessageChannel channel)>> reopen, ILog log, string server)
    {
        this.log = log;
        this.server = server;
        this.socket = socket;
        this.channel = channel;
        this.reopen = reopen;
        Fingerprint = fingerprint;
        reading = Read(channel);
    }

    private readonly ILog log;

    private readonly string server;

    private readonly Func<CancellationToken, Task<(ClientWebSocket socket, IMessageChannel channel)>> reopen;

    private readonly SynchronizationContext? context = SynchronizationContext.Current;

    private readonly ConcurrentDictionary<int, TaskCompletionSource<RemoteResponse>> pending = new();

    private readonly SemaphoreSlim writing = new(1, 1);

    private readonly CancellationTokenSource stopping = new();

    private readonly SemaphoreSlim reopening = new(1, 1);

    private ClientWebSocket socket;

    private IMessageChannel channel;

    private Task reading;

    private int nextId;

    private volatile bool lost;

    /// <inheritdoc />
    public event Action<RemoteEvent>? EventReceived;

    /// <inheritdoc />
    public event Action? Lost;

    /// <inheritdoc />
    public event Action? Reconnected;

    /// <inheritdoc />
    public string Fingerprint { get; }

    /// <inheritdoc />
    public async Task Reconnect(CancellationToken cancellation)
    {
        if (stopping.IsCancellationRequested)
        {
            return;
        }

        socket.Abort();
        await reading;
        await Reopen(cancellation);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JournalEntry>> ListEntries(DateOnly? from, DateOnly? to, CancellationToken cancellation) => (await Send(new() { Id = 0, Command = RemoteCommand.ListEntries, From = from, To = to }, cancellation)).Entries ?? throw Malformed();

    /// <inheritdoc />
    public async Task<JournalEntry?> GetEntry(DateOnly date, CancellationToken cancellation) => (await Send(new() { Id = 0, Command = RemoteCommand.GetEntry, Date = date }, cancellation)).Entry;

    /// <inheritdoc />
    public async Task<JournalEntry> SaveEntry(DateOnly date, string text, CancellationToken cancellation) => (await Send(new() { Id = 0, Command = RemoteCommand.SaveEntry, Date = date, Text = text }, cancellation)).Entry ?? throw Malformed();

    /// <inheritdoc />
    public async Task DeleteEntry(DateOnly date, CancellationToken cancellation) => await Send(new() { Id = 0, Command = RemoteCommand.DeleteEntry, Date = date }, cancellation);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Summary>> GetSummaries(SummaryKind kind, CancellationToken cancellation) => (await Send(new() { Id = 0, Command = RemoteCommand.GetSummaries, Kind = kind }, cancellation)).Summaries ?? throw Malformed();

    /// <inheritdoc />
    public async Task<ChatHistory> GetChat(CancellationToken cancellation) => (await Send(new() { Id = 0, Command = RemoteCommand.GetChat }, cancellation)).Chat ?? throw Malformed();

    /// <inheritdoc />
    public async Task SendChat(string text, CancellationToken cancellation) => await Send(new() { Id = 0, Command = RemoteCommand.SendChat, Text = text }, cancellation);

    /// <inheritdoc />
    public async Task ClearChat(CancellationToken cancellation) => await Send(new() { Id = 0, Command = RemoteCommand.ClearChat }, cancellation);

    /// <inheritdoc />
    public async Task<string> Transcribe(ReadOnlyMemory<byte> audio, CancellationToken cancellation) => (await Send(new() { Id = 0, Command = RemoteCommand.Transcribe, Data = Convert.ToBase64String(audio.Span) }, cancellation)).Transcript ?? throw Malformed();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        socket.Dispose();
        await reading;
    }

    private InvalidDataException Malformed() => new("The server sent an incomplete response.");

    private async Task<RemoteResponse> Send(RemoteRequest request, CancellationToken cancellation)
    {
        if (lost && !stopping.IsCancellationRequested)
        {
            await Reopen(cancellation);
        }

        return await Transmit(request, cancellation);
    }

    private async Task Reopen(CancellationToken cancellation)
    {
        await reopening.WaitAsync(cancellation);

        try
        {
            if (!lost || stopping.IsCancellationRequested)
            {
                return;
            }

            await reading;
            log.Write($"Reconnecting to {server}");
            (ClientWebSocket opened, IMessageChannel openedChannel) = await reopen(cancellation);
            socket.Dispose();
            socket = opened;
            channel = openedChannel;
            lost = false;
            reading = Read(openedChannel);
            Post(Reconnected);
        }
        finally
        {
            reopening.Release();
        }
    }

    private async Task<RemoteResponse> Transmit(RemoteRequest request, CancellationToken cancellation)
    {
        TaskCompletionSource<RemoteResponse> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RemoteRequest numbered = request with { Id = Interlocked.Increment(ref nextId) };
        pending[numbered.Id] = completion;

        if (lost)
        {
            pending.TryRemove(numbered.Id, out _);
            throw new IOException("The connection to the server was lost.");
        }

        await using (cancellation.Register(() => completion.TrySetCanceled(cancellation)))
        {
            try
            {
                await writing.WaitAsync(cancellation);

                try
                {
                    await channel.Send(numbered, cancellation);
                }
                finally
                {
                    writing.Release();
                }

                RemoteResponse response = await completion.Task;
                return response.Error is { } error ? throw new RemoteException(error) : response;
            }
            finally
            {
                pending.TryRemove(numbered.Id, out _);
            }
        }
    }

    private void Post(Action? handler)
    {
        if (context is null)
        {
            handler?.Invoke();
        }
        else
        {
            context.Post(_ => handler?.Invoke(), null);
        }
    }

    private async Task Recover()
    {
        for (int attempt = 0; lost && !stopping.IsCancellationRequested; attempt++)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(1 << Math.Min(attempt, 4), 15)), stopping.Token);
                await Reopen(stopping.Token);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or CertificateChangedException)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or SocketException or InvalidDataException or WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }

    private void Raise(RemoteEvent remoteEvent) => Post(() => EventReceived?.Invoke(remoteEvent));

    private async Task Read(IMessageChannel source)
    {
        try
        {
            while (await source.Receive<RemoteResponse>(stopping.Token) is { } response)
            {
                if (response.Event is { } remoteEvent)
                {
                    Raise(remoteEvent);
                }
                else if (pending.TryRemove(response.Id, out TaskCompletionSource<RemoteResponse>? completion))
                {
                    completion.TrySetResult(response);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ObjectDisposedException or OperationCanceledException or SocketException)
        {
        }
        finally
        {
            lost = true;
            log.Write(stopping.IsCancellationRequested ? $"Disconnected from {server}" : $"Connection to {server} was lost");

            if (!stopping.IsCancellationRequested)
            {
                Post(Lost);
                _ = Recover();
            }

            foreach (TaskCompletionSource<RemoteResponse> completion in pending.Values)
            {
                completion.TrySetException(new IOException("The connection to the server was lost."));
            }
        }
    }
}
