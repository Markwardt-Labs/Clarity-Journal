namespace Markwardt.ClarityJournal;

/// <summary>
/// The span of time a summary covers.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SummaryKind
{
    /// <summary>
    /// A week, Monday to Sunday.
    /// </summary>
    Week,

    /// <summary>
    /// A calendar month.
    /// </summary>
    Month,

    /// <summary>
    /// A calendar year.
    /// </summary>
    Year,

    /// <summary>
    /// Everything ever written.
    /// </summary>
    Lifetime
}

/// <summary>
/// Calendar helpers for <see cref="SummaryKind" />.
/// </summary>
public static class SummaryKindExtensions
{
    extension(SummaryKind kind)
    {
        /// <summary>
        /// Finds the period of this kind that contains a day.
        /// </summary>
        /// <param name="date">The day.</param>
        /// <returns>The period. For <see cref="SummaryKind.Lifetime" /> it is always the same single period <c>lifetime</c>, which has no calendar bounds, so its bounds are <see cref="DateOnly.MinValue" /> and <see cref="DateOnly.MaxValue" />.</returns>
        public SummaryPeriod PeriodOf(DateOnly date)
        {
            switch (kind)
            {
                case SummaryKind.Week:
                    DateOnly monday = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
                    return new SummaryPeriod { Kind = kind, Id = monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Start = monday, End = monday.AddDays(6) };
                case SummaryKind.Month:
                    DateOnly first = new(date.Year, date.Month, 1);
                    return new SummaryPeriod { Kind = kind, Id = first.ToString("yyyy-MM", CultureInfo.InvariantCulture), Start = first, End = first.AddMonths(1).AddDays(-1) };
                case SummaryKind.Year:
                    return new SummaryPeriod { Kind = kind, Id = date.Year.ToString("0000", CultureInfo.InvariantCulture), Start = new DateOnly(date.Year, 1, 1), End = new DateOnly(date.Year, 12, 31) };
                default:
                    return new SummaryPeriod { Kind = kind, Id = "lifetime", Start = DateOnly.MinValue, End = DateOnly.MaxValue };
            }
        }
    }
}
