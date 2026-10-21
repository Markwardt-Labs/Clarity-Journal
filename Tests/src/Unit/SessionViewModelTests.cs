namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class SessionViewModelTests
{
    private readonly Mock<IServerConnection> connection = new();

    private readonly SessionViewModel model;

    public SessionViewModelTests()
    {
        connection.Setup(target => target.ListEntries(It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        connection.Setup(target => target.GetSummaries(It.IsAny<SummaryKind>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        connection.Setup(target => target.GetChat(It.IsAny<CancellationToken>())).ReturnsAsync(new ChatHistory { Messages = [], Working = false });
        model = new SessionViewModel(connection.Object, new ConnectionSettings { Host = "10.0.0.5", Port = 4000, AccessKey = "key" }, "Clarity Journal 1.0.0", () => new DateOnly(2026, 10, 16));
    }

    [Fact]
    public void Tabs_StartOnTheJournalAndSwitchByCommand()
    {
        Assert.True(model.IsJournal);
        Assert.Equal("10.0.0.5:4000", model.Server);

        model.SelectTabCommand.Execute(AppTab.Chat);
        Assert.True(model.IsChat);
        Assert.False(model.IsJournal);

        model.SelectTabCommand.Execute(AppTab.Summaries);
        Assert.True(model.IsSummaries);

        model.SelectTabCommand.Execute(AppTab.Settings);
        Assert.True(model.IsSettings);
        Assert.Equal(AppTab.Settings, model.SelectedTab);
    }

    [Fact]
    public void ConnectionLossAndReturn_ShowsTheReconnectingBannerAndReadsEverythingAgain()
    {
        connection.Raise(target => target.Lost += null);
        Assert.True(model.IsDisconnected);

        connection.Raise(target => target.Reconnected += null);

        Assert.True(model.IsConnected);
        connection.Verify(target => target.GetChat(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ServerEvents_ReachEveryScreen()
    {
        connection.Raise(target => target.EventReceived += null, new RemoteEvent { Kind = RemoteEventKind.ChatMessageAdded, Message = new ChatMessage { Id = 1, Role = ChatRole.Assistant, Text = "hello", At = DateTimeOffset.UnixEpoch } });
        connection.Raise(target => target.EventReceived += null, new RemoteEvent { Kind = RemoteEventKind.Failed, Error = "boom" });

        Assert.Equal("hello", Assert.Single(model.Chat.Messages).Text);
        Assert.Equal("boom", model.Summaries.Error);
    }

    [Fact]
    public void Detach_StopsListeningToTheConnection()
    {
        model.Detach();

        connection.Raise(target => target.Lost += null);
        connection.Raise(target => target.EventReceived += null, new RemoteEvent { Kind = RemoteEventKind.ChatCleared });

        Assert.True(model.IsConnected);
    }
}
