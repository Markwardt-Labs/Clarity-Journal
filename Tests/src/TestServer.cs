namespace Markwardt.ClarityJournal.Tests;

internal sealed class TestServer : IAsyncDisposable
{
    private readonly CancellationTokenSource stopping = new();

    private readonly TempFolder? temp;

    private Task running = Task.CompletedTask;

    private TestServer(ServerConfig config, TempFolder? temp)
    {
        Config = config;
        this.temp = temp;
    }

    public ServerConfig Config { get; }

    public int Port { get; private set; }

    public string Fingerprint { get; private set; } = string.Empty;

    public ConnectionSettings Settings(string key = "secret", string? fingerprint = null) => new() { Host = "127.0.0.1", Port = Port, AccessKey = key, Fingerprint = fingerprint };

    public static async Task<TestServer> Start(IClaudeRunner claude, TimeProvider? time = null, string? dataFolder = null, Func<ServerConfig, ServerConfig>? configure = null)
    {
        TempFolder? temp = dataFolder is null ? new TempFolder() : null;
        ServerConfig config = new() { Port = 0, Key = "secret", DataFolder = dataFolder ?? temp!.Combine("data"), SpeechModel = string.Empty };
        TestServer server = new(configure?.Invoke(config) ?? config, temp);
        await server.Open(claude, time ?? TimeProvider.System);
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        await running;
        temp?.Dispose();
    }

    private async Task Open(IClaudeRunner claude, TimeProvider time)
    {
        ILog log = Mock.Of<ILog>();
        EventHub events = new();
        Database database = new();
        MetaStore meta = new(database);
        EntryStore entries = new(database);
        SummaryStore summaryStore = new(database, meta);
        JournalExport export = new(entries, summaryStore);
        Prompts prompts = new();
        Summarizer summarizer = new(entries, summaryStore, claude, prompts, export, events, time, log);
        ChatService chat = new(new ChatStore(database), meta, claude, prompts, export, events, time, log);
        RequestHandler handler = new(database, export, new JournalService(entries, export, events, time), summaryStore, chat, new SpeechService(new WhisperEngine()));
        await handler.Open(Config, stopping.Token);
        ConnectionServer server = new(new Handshake(), new IMessageChannel.Factory(), new WebSocketUpgrade(), new CertificateProvider(), handler, events, log);
        TaskCompletionSource<int> listening = new();
        TaskCompletionSource<string> certified = new();
        server.Listening += listening.SetResult;
        server.Certified += certified.SetResult;
        running = Task.WhenAll(server.Run(Config, stopping.Token), new SummaryScheduler(summarizer, summaryStore, new SummarySchedule(), time, log).Run(Config, stopping.Token), new ChatRetention(chat, time, log).Run(Config, stopping.Token));
        Port = await listening.Task;
        Fingerprint = await certified.Task;
    }
}
