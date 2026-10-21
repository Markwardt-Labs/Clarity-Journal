namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class ChatRetentionTests
{
    private readonly Mock<IChatService> chat = new();

    private readonly ServerConfig config = new() { Key = "k" };

    private readonly DateTimeOffset now = new(2026, 10, 20, 15, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Run_DeletesMessagesOlderThanTheConfiguredDaysAtOnce()
    {
        using CancellationTokenSource stop = new();
        chat.Setup(target => target.Trim(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).Callback(stop.Cancel).ReturnsAsync(4);

        await new ChatRetention(chat.Object, new FixedTimeProvider(now), Mock.Of<ILog>()).Run(config, stop.Token);

        chat.Verify(target => target.Trim(new DateTimeOffset(2026, 10, 18, 15, 30, 0, TimeSpan.Zero), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_UsesTheConfiguredRetention()
    {
        using CancellationTokenSource stop = new();
        chat.Setup(target => target.Trim(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).Callback(stop.Cancel).ReturnsAsync(0);

        await new ChatRetention(chat.Object, new FixedTimeProvider(now), Mock.Of<ILog>()).Run(config with { ChatRetentionDays = 7 }, stop.Token);

        chat.Verify(target => target.Trim(new DateTimeOffset(2026, 10, 13, 15, 30, 0, TimeSpan.Zero), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_AfterTheFirstCheckWaitsForTheNextDayInsteadOfSpinning()
    {
        using CancellationTokenSource stop = new(TimeSpan.FromMilliseconds(300));
        chat.Setup(target => target.Trim(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

        await new ChatRetention(chat.Object, new FixedTimeProvider(now), Mock.Of<ILog>()).Run(config, stop.Token);

        chat.Verify(target => target.Trim(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_AStorageFailureIsLoggedAndTheLoopCarriesOn()
    {
        using CancellationTokenSource stop = new(TimeSpan.FromMilliseconds(300));
        chat.Setup(target => target.Trim(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("disk full"));

        await new ChatRetention(chat.Object, new FixedTimeProvider(now), Mock.Of<ILog>()).Run(config, stop.Token);

        chat.Verify(target => target.Trim(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
