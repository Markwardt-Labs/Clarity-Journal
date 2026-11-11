namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// Process entry point for the command line server.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Runs the server with the given command line arguments.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        using CancellationTokenSource cancellation = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        IAppInfo info = new IAppInfo.Factory().Create(typeof(Program).Assembly);
        IReleaseFeed feed = new IReleaseFeed.Factory().Create();
        IExecutableInstaller installer = new ExecutableInstaller(feed, Environment.ProcessPath, OperatingSystem.IsWindows());
        installer.Clean();
        IServerRestart restart = new ServerRestart(cancellation, Console.Out, installer, [.. args], Environment.GetEnvironmentVariable, Launch, TimeSpan.FromSeconds(1));
        ILog log = new ConsoleLog();
        TimeProvider time = TimeProvider.System;
        IEventHub events = new EventHub();
        IDatabase database = new Database();
        IMetaStore meta = new MetaStore(database);
        IEntryStore entries = new EntryStore(database);
        ISummaryStore summaryStore = new SummaryStore(database, meta);
        IJournalExport export = new JournalExport(entries, summaryStore);
        IClaudeRunner claude = new ClaudeRunner();
        IPrompts prompts = new Prompts();
        ISummarySchedule schedule = new SummarySchedule();
        ISummarizer summarizer = new Summarizer(entries, summaryStore, claude, prompts, export, events, time, log);
        IChatService chat = new ChatService(new ChatStore(database), meta, claude, prompts, export, events, time, log);
        IRequestHandler requestHandler = new RequestHandler(database, export, new JournalService(entries, export, events, time), summaryStore, chat, new SpeechService(new WhisperEngine()), new ServerUpdater(info, feed, installer, restart));
        ISummaryScheduler scheduler = new SummaryScheduler(summarizer, summaryStore, schedule, time, log);
        IConnectionServer connectionServer = new ConnectionServer(new Handshake(info), new IMessageChannel.Factory(), new WebSocketUpgrade(), new CertificateProvider(), requestHandler, events, log);
        IApplication application = new Application(info, new ConfigLoader(), requestHandler, scheduler, new ChatRetention(chat, time, log), new EntryTitleScheduler(new EntryTitler(entries, claude, prompts, export, events, time, log), time, log), connectionServer, Console.Out);
        int code = await application.Run(args, cancellation.Token);

        if (restart.IsRequested)
        {
            Environment.Exit(restart.Relaunch());
        }

        return code;
    }

    private static string? Launch(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info)?.Dispose();
            return null;
        }
        catch (Exception exception) when (exception is Win32Exception or IOException)
        {
            return exception.Message;
        }
    }
}
