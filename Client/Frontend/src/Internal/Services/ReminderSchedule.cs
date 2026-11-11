namespace Markwardt.ClarityJournal.Client;

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
