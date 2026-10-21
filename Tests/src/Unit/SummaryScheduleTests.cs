namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class SummaryScheduleTests
{
    private readonly SummarySchedule schedule = new();

    private readonly TimeOnly evening = new(20, 0);

    [Theory]
    [InlineData("2026-10-16 19:59", "2026-10-16 20:00")]
    [InlineData("2026-10-16 20:00", "2026-10-23 20:00")]
    [InlineData("2026-10-16 23:00", "2026-10-23 20:00")]
    [InlineData("2026-10-17 09:00", "2026-10-23 20:00")]
    [InlineData("2026-10-19 00:00", "2026-10-23 20:00")]
    [InlineData("2026-10-23 21:00", "2026-10-30 20:00")]
    public void Next_IsTheFirstFridayRunStrictlyAfterTheMoment(string after, string expected)
    {
        Assert.Equal(DateTime.Parse(expected, CultureInfo.InvariantCulture), schedule.Next(DateTime.Parse(after, CultureInfo.InvariantCulture), evening));
    }

    [Theory]
    [InlineData("2026-10-16 20:00", "2026-10-16 20:00")]
    [InlineData("2026-10-16 19:59", "2026-10-09 20:00")]
    [InlineData("2026-10-18 12:00", "2026-10-16 20:00")]
    [InlineData("2026-10-22 12:00", "2026-10-16 20:00")]
    [InlineData("2026-10-23 08:00", "2026-10-16 20:00")]
    public void Previous_IsTheLatestFridayRunNotAfterTheMoment(string before, string expected)
    {
        Assert.Equal(DateTime.Parse(expected, CultureInfo.InvariantCulture), schedule.Previous(DateTime.Parse(before, CultureInfo.InvariantCulture), evening));
    }

    [Fact]
    public void NextAndPrevious_UseTheConfiguredTimeOfDay()
    {
        DateTime moment = new(2026, 10, 14, 12, 0, 0);

        Assert.Equal(new DateTime(2026, 10, 16, 6, 30, 0), schedule.Next(moment, new TimeOnly(6, 30)));
        Assert.Equal(new DateTime(2026, 10, 9, 6, 30, 0), schedule.Previous(moment, new TimeOnly(6, 30)));
    }
}
