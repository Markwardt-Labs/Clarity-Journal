namespace Markwardt.ClarityJournal.Tests;

internal sealed class FakeClaude : IClaudeRunner
{
    public FakeClaude() => Reply = request => request.Tools.Count == 0 ? $"Summary {Requests.Count}" : $"You asked: {request.Prompt}";

    private readonly object gate = new();

    private readonly List<ClaudeRequest> requests = [];

    public Func<ClaudeRequest, string> Reply { get; set; }

    public Exception? Failure { get; set; }

    public List<ClaudeRequest> Requests
    {
        get
        {
            lock (gate)
            {
                return [.. requests];
            }
        }
    }

    public Task<string> Run(ServerConfig config, ClaudeRequest request, CancellationToken cancellation)
    {
        lock (gate)
        {
            requests.Add(request);
        }

        return Failure is { } failure ? Task.FromException<string>(failure) : Task.FromResult(Reply(request));
    }
}
