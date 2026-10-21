namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// The weekly time summaries are updated: every Friday at a time of day.
/// </summary>
internal interface ISummarySchedule
{
    /// <summary>
    /// Finds the first run after a moment.
    /// </summary>
    /// <param name="after">The local moment.</param>
    /// <param name="time">The local time of day of the run.</param>
    /// <returns>The local moment of the next Friday run, strictly after <paramref name="after" />.</returns>
    DateTime Next(DateTime after, TimeOnly time);

    /// <summary>
    /// Finds the latest run that is not after a moment.
    /// </summary>
    /// <param name="before">The local moment.</param>
    /// <param name="time">The local time of day of the run.</param>
    /// <returns>The local moment of the latest Friday run at or before <paramref name="before" />.</returns>
    DateTime Previous(DateTime before, TimeOnly time);
}

/// <inheritdoc />
internal sealed class SummarySchedule : ISummarySchedule
{
    /// <inheritdoc />
    public DateTime Next(DateTime after, TimeOnly time)
    {
        DateTime candidate = Friday(after.Date, time);

        while (candidate <= after)
        {
            candidate = candidate.AddDays(7);
        }

        return candidate;
    }

    /// <inheritdoc />
    public DateTime Previous(DateTime before, TimeOnly time)
    {
        DateTime candidate = Friday(before.Date, time);

        while (candidate > before)
        {
            candidate = candidate.AddDays(-7);
        }

        return candidate;
    }

    private DateTime Friday(DateTime day, TimeOnly time) => day.AddDays(((int)DayOfWeek.Friday - (int)day.DayOfWeek + 7) % 7).Add(time.ToTimeSpan());
}
