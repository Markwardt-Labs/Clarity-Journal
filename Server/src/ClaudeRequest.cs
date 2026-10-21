namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// One run of Claude Code.
/// </summary>
internal sealed record ClaudeRequest
{
    /// <summary>
    /// Gets the prompt, which is sent on the input stream of the process.
    /// </summary>
    public required string Prompt { get; init; }

    /// <summary>
    /// Gets the system prompt.
    /// </summary>
    public required string SystemPrompt { get; init; }

    /// <summary>
    /// Gets a value indicating whether <see cref="SystemPrompt" /> replaces Claude Code's own system prompt instead of being added to it. A run without tools has no use for the coding prompt, while a run that reads files keeps it for its tool handling.
    /// </summary>
    public bool ReplacesSystemPrompt { get; init; }

    /// <summary>
    /// Gets the folder Claude Code runs in, which is also the only folder its tools can read.
    /// </summary>
    public required string Folder { get; init; }

    /// <summary>
    /// Gets the names of the tools Claude Code may use, all of them pre-approved; none by default.
    /// </summary>
    public IReadOnlyList<string> Tools { get; init; } = [];

    /// <summary>
    /// Gets the identifier of the Claude Code session to run in, or <see langword="null" /> for a run that is not kept.
    /// </summary>
    public Guid? Session { get; init; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Session" /> already exists and is continued instead of being started.
    /// </summary>
    public bool Resume { get; init; }
}
