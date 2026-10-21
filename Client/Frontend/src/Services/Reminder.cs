namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// One reminder to write the journal.
/// </summary>
public sealed record Reminder
{
    /// <summary>
    /// Gets the local time at which the reminder is shown.
    /// </summary>
    public required DateTime At { get; init; }

    /// <summary>
    /// Gets the text of the notification.
    /// </summary>
    public required string Message { get; init; }
}
