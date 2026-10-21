namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class SummarySchedulerTests
{
    private readonly Mock<ISummarizer> summarizer = new();

    private readonly Mock<ISummaryStore> summaries = new();

    private readonly ServerConfig config = new() { Key = "k" };

    private readonly FixedTimeProvider time = new(new DateTimeOffset(2026, 10, 17, 10, 0, 0, TimeSpan.Zero));

    private SummaryScheduler Create() => new(summarizer.Object, summaries.Object, new SummarySchedule(), time, Mock.Of<ILog>());

    [Fact]
    public async Task Run_NeverSummarized_RunsAtOnce()
    {
        using CancellationTokenSource stop = new();
        summaries.Setup(store => store.GetLastRun(It.IsAny<CancellationToken>())).ReturnsAsync((DateTimeOffset?)null);
        summarizer.Setup(target => target.Run(config, It.IsAny<CancellationToken>())).Callback(stop.Cancel).ReturnsAsync(true);

        await Create().Run(config, stop.Token);

        summarizer.Verify(target => target.Run(config, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_LastRunBeforeTheLastFridayEvening_CatchesUpAtOnce()
    {
        using CancellationTokenSource stop = new();
        summaries.Setup(store => store.GetLastRun(It.IsAny<CancellationToken>())).ReturnsAsync(new DateTimeOffset(2026, 10, 9, 20, 0, 5, TimeSpan.Zero));
        summarizer.Setup(target => target.Run(config, It.IsAny<CancellationToken>())).Callback(stop.Cancel).ReturnsAsync(true);

        await Create().Run(config, stop.Token);

        summarizer.Verify(target => target.Run(config, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_LastRunAfterTheLastFridayEvening_WaitsForTheNextOne()
    {
        using CancellationTokenSource stop = new(TimeSpan.FromMilliseconds(200));
        summaries.Setup(store => store.GetLastRun(It.IsAny<CancellationToken>())).ReturnsAsync(new DateTimeOffset(2026, 10, 16, 20, 0, 5, TimeSpan.Zero));

        await Create().Run(config, stop.Token);

        summarizer.Verify(target => target.Run(It.IsAny<ServerConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Run_RunFails_DoesNotSpinButWaitsBeforeRetrying()
    {
        using CancellationTokenSource stop = new(TimeSpan.FromMilliseconds(300));
        summaries.Setup(store => store.GetLastRun(It.IsAny<CancellationToken>())).ReturnsAsync((DateTimeOffset?)null);
        summarizer.Setup(target => target.Run(config, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Create().Run(config, stop.Token);

        summarizer.Verify(target => target.Run(config, It.IsAny<CancellationToken>()), Times.Once);
    }
}
