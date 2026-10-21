namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class ReminderScheduleTests
{
    private readonly IReminderSchedule schedule = new IReminderSchedule.Factory().Create();

    private DateTime At(string moment) => DateTime.Parse(moment, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("2026-10-20 00:00", "2026-10-20 12:00")]
    [InlineData("2026-10-20 11:59", "2026-10-20 12:00")]
    [InlineData("2026-10-20 12:00", "2026-10-20 20:00")]
    [InlineData("2026-10-20 15:30", "2026-10-20 20:00")]
    [InlineData("2026-10-20 19:59", "2026-10-20 20:00")]
    [InlineData("2026-10-20 20:00", "2026-10-21 12:00")]
    [InlineData("2026-10-20 23:59", "2026-10-21 12:00")]
    [InlineData("2026-12-31 21:00", "2027-01-01 12:00")]
    public void Next_IsTheFirstNoonOrEightPmStrictlyAfterTheMoment(string after, string expected)
    {
        Assert.Equal(At(expected), schedule.Next(At(after)).At);
    }

    [Fact]
    public void Next_NoonAndEveningHaveTheirOwnMessage()
    {
        Reminder noon = schedule.Next(At("2026-10-20 08:00"));
        Reminder evening = schedule.Next(At("2026-10-20 13:00"));

        Assert.NotEqual(noon.Message, evening.Message);
        Assert.False(string.IsNullOrWhiteSpace(noon.Message));
        Assert.False(string.IsNullOrWhiteSpace(evening.Message));
    }

    [Fact]
    public void Next_FollowingTheChainGivesTwoRemindersADayForever()
    {
        DateTime moment = At("2026-10-20 08:00");
        List<DateTime> times = [];

        for (int count = 0; count < 6; count++)
        {
            Reminder next = schedule.Next(moment);
            times.Add(next.At);
            moment = next.At;
        }

        Assert.Equal([At("2026-10-20 12:00"), At("2026-10-20 20:00"), At("2026-10-21 12:00"), At("2026-10-21 20:00"), At("2026-10-22 12:00"), At("2026-10-22 20:00")], times);
    }
}
