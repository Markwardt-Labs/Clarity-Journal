namespace Markwardt.ClarityJournal;

/// <summary>
/// The operations a client can ask of the server.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RemoteCommand
{
    /// <summary>
    /// Lists the entries between <see cref="RemoteRequest.From" /> and <see cref="RemoteRequest.To" />, answered in <see cref="RemoteResponse.Entries" />.
    /// </summary>
    ListEntries,

    /// <summary>
    /// Reads the entry of <see cref="RemoteRequest.Date" />, answered in <see cref="RemoteResponse.Entry" />, which is empty when the day has none.
    /// </summary>
    GetEntry,

    /// <summary>
    /// Writes <see cref="RemoteRequest.Text" /> as the entry of <see cref="RemoteRequest.Date" />, replacing any entry the day has, answered in <see cref="RemoteResponse.Entry" />.
    /// </summary>
    SaveEntry,

    /// <summary>
    /// Deletes the entry of <see cref="RemoteRequest.Date" />.
    /// </summary>
    DeleteEntry,

    /// <summary>
    /// Lists the summaries of <see cref="RemoteRequest.Kind" />, newest first, answered in <see cref="RemoteResponse.Summaries" />.
    /// </summary>
    GetSummaries,

    /// <summary>
    /// Reads the chat, answered in <see cref="RemoteResponse.Chat" />.
    /// </summary>
    GetChat,

    /// <summary>
    /// Sends <see cref="RemoteRequest.Text" /> as a question to Claude Code; returns at once and the answer arrives as a <see cref="RemoteEventKind.ChatMessageAdded" /> event.
    /// </summary>
    SendChat,

    /// <summary>
    /// Deletes the chat and starts a fresh Claude Code conversation.
    /// </summary>
    ClearChat,

    /// <summary>
    /// Turns the speech in the base64 WAV audio <see cref="RemoteRequest.Data" /> (16 kHz, mono, 16 bit) into text with the server's speech model or speech command and answers with it in <see cref="RemoteResponse.Transcript" />. Fails when voice input is turned off on the server, the audio is empty or larger than the limit, or the transcription fails.
    /// </summary>
    Transcribe
}
