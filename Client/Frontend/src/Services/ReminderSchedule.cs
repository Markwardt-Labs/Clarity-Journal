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
        public IReminderSchedule Create() => new ReminderSchedule();
    }
}

/// <inheritdoc />
internal sealed class ReminderSchedule : IReminderSchedule
{
    private readonly (TimeSpan Time, string Message)[] slots =
    [
        (TimeSpan.FromHours(12), "How is your day going? Take a moment to write it down."),
        (TimeSpan.FromHours(20), "Before the day ends, look back on it and write today's entry.")
    ];

    /// <inheritdoc />
    public Reminder Next(DateTime after)
    {
        for (int day = 0; day <= 1; day++)
        {
            foreach ((TimeSpan time, string message) in slots)
            {
                DateTime at = after.Date.AddDays(day) + time;

                if (at > after)
                {
                    return new Reminder { At = at, Message = message };
                }
            }
        }

        throw new InvalidOperationException("The schedule has no reminder.");
    }
}
