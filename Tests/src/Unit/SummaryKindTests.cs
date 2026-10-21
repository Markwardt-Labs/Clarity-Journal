namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class SummaryKindTests
{
    [Theory]
    [InlineData("2026-10-12", "2026-10-12", "2026-10-18")]
    [InlineData("2026-10-16", "2026-10-12", "2026-10-18")]
    [InlineData("2026-10-18", "2026-10-12", "2026-10-18")]
    [InlineData("2026-10-19", "2026-10-19", "2026-10-25")]
    [InlineData("2026-01-01", "2025-12-29", "2026-01-04")]
    public void PeriodOf_Week_RunsMondayToSunday(string day, string start, string end)
    {
        SummaryPeriod period = SummaryKind.Week.PeriodOf(DateOnly.Parse(day, CultureInfo.InvariantCulture));

        Assert.Equal(start, period.Id);
        Assert.Equal(DateOnly.Parse(start, CultureInfo.InvariantCulture), period.Start);
        Assert.Equal(DateOnly.Parse(end, CultureInfo.InvariantCulture), period.End);
    }

    [Theory]
    [InlineData("2026-10-16", "2026-10", "2026-10-01", "2026-10-31")]
    [InlineData("2024-02-10", "2024-02", "2024-02-01", "2024-02-29")]
    [InlineData("2026-02-28", "2026-02", "2026-02-01", "2026-02-28")]
    [InlineData("2026-12-31", "2026-12", "2026-12-01", "2026-12-31")]
    public void PeriodOf_Month_CoversTheCalendarMonth(string day, string id, string start, string end)
    {
        SummaryPeriod period = SummaryKind.Month.PeriodOf(DateOnly.Parse(day, CultureInfo.InvariantCulture));

        Assert.Equal(id, period.Id);
        Assert.Equal(DateOnly.Parse(start, CultureInfo.InvariantCulture), period.Start);
        Assert.Equal(DateOnly.Parse(end, CultureInfo.InvariantCulture), period.End);
    }

    [Fact]
    public void PeriodOf_Year_CoversTheCalendarYear()
    {
        SummaryPeriod period = SummaryKind.Year.PeriodOf(new DateOnly(2026, 6, 15));

        Assert.Equal("2026", period.Id);
        Assert.Equal(new DateOnly(2026, 1, 1), period.Start);
        Assert.Equal(new DateOnly(2026, 12, 31), period.End);
    }

    [Fact]
    public void PeriodOf_Lifetime_IsOneSinglePeriodForEveryDay()
    {
        Assert.Equal(SummaryKind.Lifetime.PeriodOf(new DateOnly(2020, 1, 1)), SummaryKind.Lifetime.PeriodOf(new DateOnly(2026, 10, 16)));
        Assert.Equal("lifetime", SummaryKind.Lifetime.PeriodOf(new DateOnly(2026, 10, 16)).Id);
    }

    [Fact]
    public void PeriodOf_SamePeriodFromDifferentDays_IsEqualSoItGroups()
    {
        Assert.Equal(SummaryKind.Week.PeriodOf(new DateOnly(2026, 10, 14)), SummaryKind.Week.PeriodOf(new DateOnly(2026, 10, 17)));
    }
}
