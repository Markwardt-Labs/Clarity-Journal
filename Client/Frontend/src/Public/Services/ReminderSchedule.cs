namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// When the app reminds the user to write the journal: every day at noon and at 8 pm.
/// </summary>
public interface IReminderSchedule
{
    /// <summary>
    /// Finds the first reminder after a moment.
    /// </summary>
    /// <param name="after">The local moment.</param>
    /// <returns>The reminder, strictly after <paramref name="after" />.</returns>
    Reminder Next(DateTime after);

    /// <summary>
    /// Creates <see cref="IReminderSchedule" /> instances.
    /// </summary>
    public interface IFactory
    {
        /// <summary>
        /// Creates the schedule.
        /// </summary>
        /// <returns>The schedule.</returns>
        IReminderSchedule Create();
    }

    /// <inheritdoc />
    public sealed class Factory : IFactory
    {
        /// <inheritdoc />
        public IReminderSchedule Create()
            => new ReminderSchedule();
    }
}
