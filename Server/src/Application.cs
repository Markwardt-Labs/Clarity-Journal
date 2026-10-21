namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// The server's top-level run loop.
/// </summary>
internal interface IApplication
{
    /// <summary>
    /// Runs the server until it completes or is cancelled.
    /// </summary>
    /// <param name="args">The command line arguments; the first, if any, is the config file path (default <c>config.json</c>).</param>
    /// <param name="cancellation">Cancels the run.</param>
    /// <returns>The process exit code.</returns>
    Task<int> Run(IReadOnlyList<string> args, CancellationToken cancellation);
}

/// <inheritdoc />
internal sealed class Application(IAppInfo info, IConfigLoader configLoader, IRequestHandler requestHandler, ISummaryScheduler scheduler, IChatRetention retention, IConnectionServer connectionServer, TextWriter output) : IApplication
{
    /// <inheritdoc />
    public async Task<int> Run(IReadOnlyList<string> args, CancellationToken cancellation)
    {
        await output.WriteLineAsync($"{info.Name} server {info.Version}".AsMemory(), cancellation);

        try
        {
            ServerConfig config = await configLoader.Load(args.Count > 0 ? args[0] : "config.json", cancellation);
            await requestHandler.Open(config, cancellation);
            connectionServer.Certified += fingerprint => output.WriteLine($"Certificate SHA-256: {fingerprint}");
            connectionServer.Listening += port => output.WriteLine($"Listening on port {port} (wss)");

            using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            Task serving = connectionServer.Run(config, stop.Token);
            Task scheduling = scheduler.Run(config, stop.Token);
            Task trimming = retention.Run(config, stop.Token);
            await Task.WhenAny(serving, scheduling, trimming);
            await stop.CancelAsync();
            await Task.WhenAll(serving, scheduling, trimming);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or SocketException or SqliteException)
        {
            await output.WriteLineAsync(exception.Message.AsMemory(), cancellation);
            return 1;
        }
    }
}
