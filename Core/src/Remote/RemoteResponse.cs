namespace Markwardt.ClarityJournal;

/// <summary>
/// The server's reply to one <see cref="RemoteRequest" />, sent as a single JSON line.
/// </summary>
public sealed record RemoteResponse
{
    /// <summary>
    /// Gets the <see cref="RemoteRequest.Id" /> this answers.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// Gets the failure message, or <see langword="null" /> when the request succeeded.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets the entries answering <see cref="RemoteCommand.ListEntries" />, oldest first.
    /// </summary>
    public IReadOnlyList<JournalEntry>? Entries { get; init; }

    /// <summary>
    /// Gets the entry answering <see cref="RemoteCommand.GetEntry" /> or <see cref="RemoteCommand.SaveEntry" />; empty for a <see cref="RemoteCommand.GetEntry" /> of a day without one.
    /// </summary>
    public JournalEntry? Entry { get; init; }

    /// <summary>
    /// Gets the summaries answering <see cref="RemoteCommand.GetSummaries" />.
    /// </summary>
    public IReadOnlyList<Summary>? Summaries { get; init; }

    /// <summary>
    /// Gets the chat answering <see cref="RemoteCommand.GetChat" />.
    /// </summary>
    public ChatHistory? Chat { get; init; }

    /// <summary>
    /// Gets the text of the speech answering <see cref="RemoteCommand.Transcribe" />, empty when nothing was heard.
    /// </summary>
    public string? Transcript { get; init; }

    /// <summary>
    /// Gets the event the server pushes; set only on a response with id zero, which answers no request.
    /// </summary>
    public RemoteEvent? Event { get; init; }
}
