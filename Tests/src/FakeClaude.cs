namespace Markwardt.ClarityJournal.Tests;

internal sealed class FakeClaude : IClaudeRunner
{
    public FakeClaude()
        => Reply = request => request.SystemPrompt == new Prompts().TitleSystem ? Title(request) : request.Tools.Count == 0 ? $"Summary {Requests.Count}" : $"You asked: {request.Prompt}";

    private readonly object gate = new();

    private readonly List<ClaudeRequest> requests = [];

    public Func<ClaudeRequest, string> Reply { get; set; }

    public List<ClaudeRequest> TitleRequests => [.. Requests.Where(request => request.SystemPrompt == new Prompts().TitleSystem)];

    private string Title(ClaudeRequest request)
    {
        string date = System.Text.RegularExpressions.Regex.Match(request.Prompt, @"\d{4}-\d{2}-\d{2}").Value;
        return $"{{\"title\": \"Title of {date}\", \"summary\": \"Summary of {date}.\"}}";
    }

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

    public Task<string> Run(ServerConfig config, ClaudeRequest request, CancellationToken cancellation = default)
    {
        lock (gate)
        {
            requests.Add(request);
        }

        return Failure is { } failure ? Task.FromException<string>(failure) : Task.FromResult(Reply(request));
    }
}
