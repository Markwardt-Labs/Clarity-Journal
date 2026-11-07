namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class EntryTitleSchedulerTests
{
    private readonly Mock<IEntryTitler> titler = new();

    private readonly ServerConfig config = new() { Key = "k" };

    private int runs;

    private async Task<bool> Until(Func<bool> done)
    {
        for (int attempt = 0; attempt < 200 && !done(); attempt++)
        {
            await Task.Delay(10);
        }

        return done();
    }

    private (Task Run, CancellationTokenSource Stop) Start(ManualTimeProvider time, bool succeeds = true)
    {
        titler.Setup(target => target.Run(config, It.IsAny<CancellationToken>())).Callback(() => Interlocked.Increment(ref runs)).ReturnsAsync(() => succeeds);
        CancellationTokenSource stop = new();
        return (new EntryTitleScheduler(titler.Object, time, Mock.Of<ILog>()).Run(config, stop.Token), stop);
    }

    [Fact]
    public async Task Run_TitlesAtOnceWhenTheServerStartsThenAfterEveryMidnight()
    {
        ManualTimeProvider time = new(new DateTimeOffset(2026, 10, 16, 15, 0, 0, TimeSpan.Zero));
        (Task run, CancellationTokenSource stop) = Start(time);

        Assert.True(await Until(() => runs == 1));

        time.Advance(TimeSpan.FromHours(8) + TimeSpan.FromMinutes(59));
        await Task.Delay(50);
        Assert.Equal(1, runs);

        time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(10));
        Assert.True(await Until(() => runs == 2));

        time.Advance(TimeSpan.FromHours(24));
        Assert.True(await Until(() => runs == 3));

        await stop.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Run_AFailedRunIsTriedAgainAnHourLater()
    {
        ManualTimeProvider time = new(new DateTimeOffset(2026, 10, 16, 15, 0, 0, TimeSpan.Zero));
        (Task run, CancellationTokenSource stop) = Start(time, false);
        Assert.True(await Until(() => runs == 1));

        time.Advance(TimeSpan.FromMinutes(59));
        await Task.Delay(50);
        Assert.Equal(1, runs);

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await Until(() => runs == 2));

        await stop.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Run_AFailureNearMidnightWaitsForTheNewDayInsteadOfAnHour()
    {
        ManualTimeProvider time = new(new DateTimeOffset(2026, 10, 16, 23, 30, 0, TimeSpan.Zero));
        (Task run, CancellationTokenSource stop) = Start(time, false);
        Assert.True(await Until(() => runs == 1));

        time.Advance(TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(6));

        Assert.True(await Until(() => runs == 2));
        await stop.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Run_StopsCleanlyWhenCancelled()
    {
        ManualTimeProvider time = new(new DateTimeOffset(2026, 10, 16, 15, 0, 0, TimeSpan.Zero));
        (Task run, CancellationTokenSource stop) = Start(time);
        Assert.True(await Until(() => runs == 1));

        await stop.CancelAsync();
        await run;

        Assert.True(run.IsCompletedSuccessfully);
    }
}
