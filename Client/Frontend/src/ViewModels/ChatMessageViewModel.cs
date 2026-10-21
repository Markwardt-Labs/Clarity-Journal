namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// One message of the chat, shown as a bubble.
/// </summary>
/// <param name="message">The message to show.</param>
internal sealed class ChatMessageViewModel(ChatMessage message)
{
    /// <summary>
    /// Gets the server-assigned identifier of the message.
    /// </summary>
    public long Id { get; } = message.Id;

    /// <summary>
    /// Gets the message text; markdown for an assistant message.
    /// </summary>
    public string Text { get; } = message.Text;

    /// <summary>
    /// Gets a value indicating whether the user wrote the message.
    /// </summary>
    public bool IsUser { get; } = message.Role == ChatRole.User;

    /// <summary>
    /// Gets a value indicating whether Claude Code wrote the message.
    /// </summary>
    public bool IsAssistant => !IsUser;

    /// <summary>
    /// Gets the time the message was written, such as <c>8:05 PM</c>.
    /// </summary>
    public string Time { get; } = message.At.ToLocalTime().ToString("h:mm tt", CultureInfo.InvariantCulture);
}
