namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// Carries out the requests of an authenticated client.
/// </summary>
internal interface IRequestHandler
{
    /// <summary>
    /// Prepares the server to serve requests: creates the database and the files Claude Code reads.
    /// </summary>
    /// <param name="config">The server settings.</param>
    /// <param name="cancellation">Cancels the setup.</param>
    Task Open(ServerConfig config, CancellationToken cancellation);

    /// <summary>
    /// Carries out one request; failures are reported in the response instead of thrown.
    /// </summary>
    /// <param name="request">The client request.</param>
    /// <param name="cancellation">Cancels the request; also ends the background work the request starts, so it is the server's lifetime.</param>
    /// <returns>The response to send back.</returns>
    Task<RemoteResponse> Handle(RemoteRequest request, CancellationToken cancellation);
}

/// <inheritdoc />
internal sealed class RequestHandler(IDatabase database, IJournalExport export, IJournalService journal, ISummaryStore summaries, IChatService chat, ISpeechService speech) : IRequestHandler
{
    private ServerConfig? config;

    /// <inheritdoc />
    public async Task Open(ServerConfig config, CancellationToken cancellation)
    {
        this.config = config;
        speech.Configure(config);
        await database.Initialize(config.DataFolder, cancellation);
        await export.Open(config.DataFolder, cancellation);
    }

    /// <inheritdoc />
    public async Task<RemoteResponse> Handle(RemoteRequest request, CancellationToken cancellation)
    {
        ServerConfig current = config ?? throw new InvalidOperationException("The handler is not open.");

        try
        {
            RemoteResponse response = new() { Id = request.Id };

            switch (request.Command)
            {
                case RemoteCommand.ListEntries:
                    return response with { Entries = await journal.List(request.From, request.To, cancellation) };
                case RemoteCommand.GetEntry:
                    return response with { Entry = await journal.Get(Require(request.Date, "date"), cancellation) };
                case RemoteCommand.SaveEntry:
                    return response with { Entry = await journal.Save(Require(request.Date, "date"), Require(request.Text, "text"), cancellation) };
                case RemoteCommand.DeleteEntry:
                    await journal.Delete(Require(request.Date, "date"), cancellation);
                    return response;
                case RemoteCommand.GetSummaries:
                    return response with { Summaries = await summaries.List(Require(request.Kind, "kind"), cancellation) };
                case RemoteCommand.GetChat:
                    return response with { Chat = await chat.Get(cancellation) };
                case RemoteCommand.SendChat:
                    await chat.Send(current, Require(request.Text, "text"), cancellation);
                    return response;
                case RemoteCommand.ClearChat:
                    await chat.Clear(cancellation);
                    return response;
                case RemoteCommand.Transcribe:
                    return response with { Transcript = await speech.Transcribe(Require(request.Data, "data"), cancellation) };
                default:
                    throw new RequestException("The server does not know that command.");
            }
        }
        catch (RequestException exception)
        {
            return new RemoteResponse { Id = request.Id, Error = exception.Message };
        }
    }

    private T Require<T>(T? value, string name)
        where T : struct => value ?? throw new RequestException($"The request needs a {name}.");

    private string Require(string? value, string name) => value ?? throw new RequestException($"The request needs a {name}.");
}
