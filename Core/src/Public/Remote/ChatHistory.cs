namespace Markwardt.ClarityJournal;

/// <summary>
/// The whole chat about the journal.
/// </summary>
public sealed record ChatHistory
{
    /// <summary>
    /// Gets the messages, oldest first.
    /// </summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>
    /// Gets a value indicating whether Claude Code is working on an answer.
    /// </summary>
    public required bool Working { get; init; }
}
