namespace Markwardt.ClarityJournal;

/// <summary>
/// Who wrote a chat message.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChatRole
{
    /// <summary>
    /// The person using the app.
    /// </summary>
    User,

    /// <summary>
    /// Claude Code on the server.
    /// </summary>
    Assistant
}
