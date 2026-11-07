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
    /// Gets the day before which <see cref="RemoteCommand.ListDigests" /> lists, or <see langword="null" /> to start with the newest day.
    /// </summary>
    public DateOnly? Before { get; init; }

    /// <summary>
    /// Gets how many days <see cref="RemoteCommand.ListDigests" /> lists at most, or <see langword="null" /> for the server's default; the server caps it.
    /// </summary>
    public int? Limit { get; init; }

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
