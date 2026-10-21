namespace Markwardt.ClarityJournal;

/// <summary>
/// One message of the chat about the journal.
/// </summary>
public sealed record ChatMessage
{
    /// <summary>
    /// Gets the server-assigned identifier, which grows with every message so it also gives the order.
    /// </summary>
    public required long Id { get; init; }

    /// <summary>
    /// Gets who wrote the message.
    /// </summary>
    public required ChatRole Role { get; init; }

    /// <summary>
    /// Gets the message text; markdown for an assistant message.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets when the message was written.
    /// </summary>
    public required DateTimeOffset At { get; init; }
}
