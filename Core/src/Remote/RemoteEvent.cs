namespace Markwardt.ClarityJournal;

/// <summary>
/// Something the server pushes to every connected client without being asked, sent in <see cref="RemoteResponse.Event" /> of a response with id zero.
/// </summary>
public sealed record RemoteEvent
{
    /// <summary>
    /// Gets what happened.
    /// </summary>
    public required RemoteEventKind Kind { get; init; }

    /// <summary>
    /// Gets the day whose entry was saved or deleted, for <see cref="RemoteEventKind.EntryChanged" />.
    /// </summary>
    public DateOnly? Date { get; init; }

    /// <summary>
    /// Gets the added message, for <see cref="RemoteEventKind.ChatMessageAdded" />.
    /// </summary>
    public ChatMessage? Message { get; init; }

    /// <summary>
    /// Gets a value indicating whether Claude Code is working on an answer, for <see cref="RemoteEventKind.ChatWorkingChanged" />.
    /// </summary>
    public bool? Working { get; init; }

    /// <summary>
    /// Gets the failure message, for <see cref="RemoteEventKind.Failed" />.
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// The kinds of <see cref="RemoteEvent" />.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RemoteEventKind
{
    /// <summary>
    /// An entry was saved or deleted, possibly from another device.
    /// </summary>
    EntryChanged,

    /// <summary>
    /// A summary was written or updated, so the summaries should be read again.
    /// </summary>
    SummariesChanged,

    /// <summary>
    /// A message was added to the chat.
    /// </summary>
    ChatMessageAdded,

    /// <summary>
    /// Claude Code started or stopped working on a chat answer.
    /// </summary>
    ChatWorkingChanged,

    /// <summary>
    /// The chat was cleared.
    /// </summary>
    ChatCleared,

    /// <summary>
    /// The server deleted old messages from the chat, so the chat should be read again.
    /// </summary>
    ChatTrimmed,

    /// <summary>
    /// Claude Code could not do the work it was given.
    /// </summary>
    Failed
}
