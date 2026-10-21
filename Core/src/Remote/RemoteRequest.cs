namespace Markwardt.ClarityJournal;

/// <summary>
/// One client request, sent as a single JSON line after the handshake.
/// </summary>
public sealed record RemoteRequest
{
    /// <summary>
    /// Gets the client-chosen number echoed in the matching <see cref="RemoteResponse" />, so requests can be in flight together.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// Gets the operation to perform.
    /// </summary>
    public required RemoteCommand Command { get; init; }

    /// <summary>
    /// Gets the day the command targets, if any.
    /// </summary>
    public DateOnly? Date { get; init; }

    /// <summary>
    /// Gets the first day <see cref="RemoteCommand.ListEntries" /> includes, or <see langword="null" /> for no lower bound.
    /// </summary>
    public DateOnly? From { get; init; }

    /// <summary>
    /// Gets the last day <see cref="RemoteCommand.ListEntries" /> includes, or <see langword="null" /> for no upper bound.
    /// </summary>
    public DateOnly? To { get; init; }

    /// <summary>
    /// Gets the kind of summaries <see cref="RemoteCommand.GetSummaries" /> lists.
    /// </summary>
    public SummaryKind? Kind { get; init; }

    /// <summary>
    /// Gets the entry text for <see cref="RemoteCommand.SaveEntry" /> or the question for <see cref="RemoteCommand.SendChat" />.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Gets the base64 WAV recording for <see cref="RemoteCommand.Transcribe" />.
    /// </summary>
    public string? Data { get; init; }
}
