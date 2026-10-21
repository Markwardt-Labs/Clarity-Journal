namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class MainViewModelTests
{
    private readonly Mock<IServerConnection.IFactory> factory = new();

    private readonly Mock<IServerConnection> connection = new();

    private readonly Mock<ISettingsStore> store = new();

    private readonly ConnectionSettings saved = new() { Host = "host", Port = 52681, AccessKey = "key", Fingerprint = "ABCD" };

    private readonly MainViewModel model;

    public MainViewModelTests()
    {
        connection.SetupGet(target => target.Fingerprint).Returns("ABCD");
        connection.Setup(target => target.ListEntries(It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        connection.Setup(target => target.GetSummaries(It.IsAny<SummaryKind>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        connection.Setup(target => target.GetChat(It.IsAny<CancellationToken>())).ReturnsAsync(new ChatHistory { Messages = [], Working = false });
        store.Setup(target => target.Load(It.IsAny<CancellationToken>())).ReturnsAsync(saved);
        model = new MainViewModel(new IAppInfo.Factory().Create(typeof(MainViewModelTests).Assembly), factory.Object, store.Object);
    }

    [Fact]
    public async Task Start_WithoutASavedServer_ShowsTheConnectScreen()
    {
        store.Setup(target => target.Load(It.IsAny<CancellationToken>())).ReturnsAsync((ConnectionSettings?)null);

        await model.Start();

        Assert.IsType<ConnectViewModel>(model.Current);
        factory.Verify(target => target.Connect(It.IsAny<ConnectionSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Start_WithASavedServer_ConnectsAndLoadsEveryScreen()
    {
        factory.Setup(target => target.Connect(saved, It.IsAny<CancellationToken>())).ReturnsAsync(connection.Object);

        await model.Start();

        SessionViewModel session = Assert.IsType<SessionViewModel>(model.Current);
        Assert.Equal("host:52681", session.Server);
        connection.Verify(target => target.ListEntries(It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Once);
        connection.Verify(target => target.GetSummaries(SummaryKind.Week, It.IsAny<CancellationToken>()), Times.Once);
        connection.Verify(target => target.GetChat(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Start_SavedServerUnreachable_ShowsTheConnectScreenWithTheReason()
    {
        factory.Setup(target => target.Connect(saved, It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("Timed out waiting for the server."));

        await model.Start();

        ConnectViewModel connect = Assert.IsType<ConnectViewModel>(model.Current);
        Assert.Equal("Timed out waiting for the server.", connect.Error);
        Assert.Equal("host", connect.Host);
    }

    [Fact]
    public async Task Start_CertificateChanged_OffersToTrustTheNewOne()
    {
        factory.Setup(target => target.Connect(saved, It.IsAny<CancellationToken>())).ThrowsAsync(new CertificateChangedException("changed"));

        await model.Start();

        Assert.True(Assert.IsType<ConnectViewModel>(model.Current).CertificateChanged);
    }

    [Fact]
    public async Task Connecting_SavesTheSettingsWithTheServersFingerprintAndOpensTheSession()
    {
        store.Setup(target => target.Load(It.IsAny<CancellationToken>())).ReturnsAsync((ConnectionSettings?)null);
        factory.Setup(target => target.Connect(It.IsAny<ConnectionSettings>(), It.IsAny<CancellationToken>())).ReturnsAsync(connection.Object);
        await model.Start();
        ConnectViewModel connect = Assert.IsType<ConnectViewModel>(model.Current);
        (connect.Host, connect.AccessKey) = ("host", "key");

        connect.ConnectCommand.Execute(null);

        store.Verify(target => target.Save(It.Is<ConnectionSettings>(settings => settings.Host == "host" && settings.Fingerprint == "ABCD"), It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsType<SessionViewModel>(model.Current);
    }

    [Fact]
    public async Task Disconnect_ClosesTheConnectionAndReturnsToTheConnectScreenWithTheSavedServer()
    {
        factory.Setup(target => target.Connect(saved, It.IsAny<CancellationToken>())).ReturnsAsync(connection.Object);
        await model.Start();

        Assert.IsType<SessionViewModel>(model.Current).DisconnectCommand.Execute(null);

        connection.Verify(target => target.DisposeAsync(), Times.Once);
        Assert.Equal("host", Assert.IsType<ConnectViewModel>(model.Current).Host);
    }

    [Fact]
    public async Task Resume_ReconnectsAnOpenConnectionAndDoesNothingWithoutOne()
    {
        await model.Resume();
        connection.Verify(target => target.Reconnect(It.IsAny<CancellationToken>()), Times.Never);

        factory.Setup(target => target.Connect(saved, It.IsAny<CancellationToken>())).ReturnsAsync(connection.Object);
        await model.Start();
        await model.Resume();

        connection.Verify(target => target.Reconnect(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resume_WhenTheServerCannotBeReached_KeepsTheScreen()
    {
        factory.Setup(target => target.Connect(saved, It.IsAny<CancellationToken>())).ReturnsAsync(connection.Object);
        connection.Setup(target => target.Reconnect(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("down"));
        await model.Start();

        await model.Resume();

        Assert.IsType<SessionViewModel>(model.Current);
    }

    [Fact]
    public async Task Flush_SendsTextThatTheAutomaticSaveHasNotSentYet()
    {
        connection.Setup(target => target.SaveEntry(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DateOnly date, string text, CancellationToken _) => new JournalEntry { Date = date, Text = text, UpdatedAt = DateTimeOffset.UnixEpoch });
        factory.Setup(target => target.Connect(saved, It.IsAny<CancellationToken>())).ReturnsAsync(connection.Object);
        await model.Start();
        SessionViewModel session = Assert.IsType<SessionViewModel>(model.Current);
        session.Journal.EntryText = "written just before leaving";

        await model.Flush();

        connection.Verify(target => target.SaveEntry(session.Journal.SelectedDate, "written just before leaving", It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(session.Journal.IsDirty);
    }

    [Fact]
    public async Task Flush_WithoutASession_DoesNothing()
    {
        await model.Flush();

        connection.Verify(target => target.SaveEntry(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
