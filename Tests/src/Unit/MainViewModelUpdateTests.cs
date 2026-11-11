namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class MainViewModelUpdateTests
{
    private readonly Mock<IServerConnection.IFactory> factory = new();

    private readonly Mock<IServerConnection> connection = new();

    private readonly Mock<ISettingsStore> store = new();

    private readonly Mock<IReleaseFeed> feed = new();

    private readonly Mock<IClientUpdater> updater = new();

    private readonly Mock<IAppInfo> info = new();

    private readonly ConnectionSettings saved = new() { Host = "host", Port = 52681, AccessKey = "key", Fingerprint = "ABCD" };

    private readonly List<string> calls = [];

    private string? serverVersion = "1.0.0";

    private MainViewModel model = null!;

    public MainViewModelUpdateTests()
    {
        info.SetupGet(target => target.Name).Returns("Clarity Journal");
        info.SetupGet(target => target.Version).Returns("1.0.0");
        connection.SetupGet(target => target.Fingerprint).Returns("ABCD");
        connection.SetupGet(target => target.ServerVersion).Returns(() => serverVersion);
        connection.Setup(target => target.GetEntry(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync((JournalEntry?)null);
        connection.Setup(target => target.GetSummaries(It.IsAny<SummaryKind>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        connection.Setup(target => target.GetChat(It.IsAny<CancellationToken>())).ReturnsAsync(new ChatHistory { Messages = [], Working = false });
        connection.Setup(target => target.SaveEntry(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DateOnly date, string text, CancellationToken _) =>
        {
            calls.Add("save");
            return new JournalEntry { Date = date, Text = text, UpdatedAt = DateTimeOffset.UnixEpoch };
        });
        connection.Setup(target => target.UpdateServer(It.IsAny<CancellationToken>())).Returns(() =>
        {
            calls.Add("server");
            return Task.CompletedTask;
        });
        updater.SetupGet(target => target.IsSupported).Returns(true);
        updater.Setup(target => target.Install(It.IsAny<ReleaseInfo>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>())).Returns(() =>
        {
            calls.Add("app");
            return Task.CompletedTask;
        });
        feed.Setup(target => target.Latest(It.IsAny<CancellationToken>())).ReturnsAsync(new ReleaseInfo { Version = new AppVersion { Major = 1, Minor = 1, Patch = 0 }, Assets = [] });
        store.Setup(target => target.Load(It.IsAny<CancellationToken>())).ReturnsAsync(saved);
        factory.Setup(target => target.Connect(saved, It.IsAny<CancellationToken>())).ReturnsAsync(connection.Object);
        model = Create(feed.Object, updater.Object);
    }

    private MainViewModel Create(IReleaseFeed? source, IClientUpdater? installer, TimeSpan? retry = null)
        => new(info.Object, factory.Object, store.Object, null, source, installer, retry ?? TimeSpan.FromMilliseconds(20));

    private async Task<bool> Until(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 400 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        return condition();
    }

    private async Task<(SessionViewModel Session, UpdateViewModel Update)> Offered()
    {
        await model.Start();
        SessionViewModel session = Assert.IsType<SessionViewModel>(model.Current);
        UpdateViewModel update = session.Update!;
        Assert.True(await Until(() => update.IsAvailable));
        return (session, update);
    }

    [Fact]
    public async Task Session_OffersTheUpdateOnceTheNewerReleaseIsFound()
    {
        await model.Start();
        SessionViewModel session = Assert.IsType<SessionViewModel>(model.Current);

        Assert.NotNull(session.Update);
        Assert.True(await Until(() => session.Update!.IsAvailable));
        Assert.Equal("Update to 1.1.0", session.Update!.ButtonText);
    }

    [Fact]
    public async Task Session_WithoutAFeedNeverOffersAnUpdate()
    {
        model = Create(null, updater.Object);

        await model.Start();
        await Task.Delay(100);

        Assert.False(Assert.IsType<SessionViewModel>(model.Current).Update!.IsAvailable);
    }

    [Fact]
    public async Task Update_TakesOverTheScreenWhileItRunsAndGivesTheSessionBackWhenClosed()
    {
        (SessionViewModel session, UpdateViewModel update) = await Offered();
        update.Begin();
        Assert.Same(session, model.Current);

        update.ConfirmCommand.Execute(null);
        Assert.True(await Until(() => update.Phase == UpdatePhase.Finished));

        Assert.Same(update, model.Current);
        update.CloseCommand.Execute(null);
        Assert.Same(session, model.Current);
    }

    [Fact]
    public async Task Update_ForTheServerOnlyGivesTheSessionBackByItselfOnceTheServerIsBack()
    {
        model = Create(feed.Object, null);
        (SessionViewModel session, UpdateViewModel update) = await Offered();
        update.Begin();

        update.ConfirmCommand.Execute(null);
        Assert.True(await Until(() => update.Phase == UpdatePhase.Waiting));
        Assert.Same(update, model.Current);

        serverVersion = "1.1.0";
        connection.Raise(target => target.Reconnected += null);

        Assert.Same(session, model.Current);
        Assert.True(await Until(() => !update.IsAvailable));
    }

    [Fact]
    public async Task Update_SavesTheEntryBeforeTheServerIsRestartedAndTheAppReplaced()
    {
        (SessionViewModel session, UpdateViewModel update) = await Offered();
        session.Journal.EntryText = "Typed a moment ago";
        update.Begin();

        update.ConfirmCommand.Execute(null);
        Assert.True(await Until(() => update.Phase == UpdatePhase.Finished));

        Assert.Equal(["save", "server", "app"], calls);
        connection.Verify(target => target.SaveEntry(session.Journal.SelectedDate, "Typed a moment ago", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_IsRefusedWhenTheEntryCannotBeSavedAndTheSessionComesBackWithTheError()
    {
        (SessionViewModel session, UpdateViewModel update) = await Offered();
        session.Journal.EntryText = "Not yet saved";
        connection.Setup(target => target.SaveEntry(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("offline"));
        update.Begin();

        update.ConfirmCommand.Execute(null);
        Assert.True(await Until(() => update.HasError));

        Assert.Equal("Today's entry could not be saved, so the update did not start.", update.Error);
        Assert.Equal(UpdatePhase.Confirming, update.Phase);
        Assert.Same(session, model.Current);
        Assert.Equal("Not yet saved", session.Journal.EntryText);
        Assert.DoesNotContain("server", calls);
        Assert.DoesNotContain("app", calls);
    }

    [Fact]
    public async Task Update_IsNotHeldBackByAnEntryThatHasNothingToSave()
    {
        (_, UpdateViewModel update) = await Offered();
        update.Begin();

        update.ConfirmCommand.Execute(null);
        Assert.True(await Until(() => update.Phase == UpdatePhase.Finished));

        Assert.Equal(["server", "app"], calls);
    }

    [Fact]
    public async Task Disconnecting_StopsLookingForUpdatesAndReturnsToTheConnectScreen()
    {
        (SessionViewModel session, _) = await Offered();
        int lookups = feed.Invocations.Count;

        session.DisconnectCommand.Execute(null);
        Assert.True(await Until(() => model.Current is ConnectViewModel));
        await Task.Delay(120);

        Assert.Equal(lookups, feed.Invocations.Count);
        connection.Verify(target => target.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Start_WhenTheSavedServerIsNotBackYetKeepsTryingAndOpensTheSessionWhenItAnswers()
    {
        int attempts = 0;
        factory.Setup(target => target.Connect(saved, It.IsAny<CancellationToken>())).Returns(() => ++attempts < 4 ? Task.FromException<IServerConnection>(new IOException("Could not connect to host:52681")) : Task.FromResult(connection.Object));

        await model.Start();
        ConnectViewModel connect = Assert.IsType<ConnectViewModel>(model.Current);
        Assert.Equal("Could not connect to host:52681", connect.Error);

        Assert.True(await Until(() => model.Current is SessionViewModel));

        Assert.Equal(4, attempts);
    }

    [Fact]
    public async Task Start_DoesNotKeepTryingWhenTheKeyIsRejected()
    {
        factory.Setup(target => target.Connect(saved, It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException("The server rejected the access key."));

        await model.Start();
        await Task.Delay(150);

        Assert.IsType<ConnectViewModel>(model.Current);
        factory.Verify(target => target.Connect(saved, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Start_DoesNotKeepTryingWhenTheCertificateChanged()
    {
        factory.Setup(target => target.Connect(saved, It.IsAny<CancellationToken>())).ThrowsAsync(new CertificateChangedException("changed"));

        await model.Start();
        await Task.Delay(150);

        Assert.True(Assert.IsType<ConnectViewModel>(model.Current).CertificateChanged);
        factory.Verify(target => target.Connect(saved, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Start_StopsTryingOnceTheUserTouchesTheConnectScreen()
    {
        factory.Setup(target => target.Connect(saved, It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("offline"));

        await model.Start();
        ConnectViewModel connect = Assert.IsType<ConnectViewModel>(model.Current);
        connect.Host = "another-host";
        await Task.Delay(200);

        Assert.Same(connect, model.Current);
        Assert.True(factory.Invocations.Count(call => call.Method.Name == nameof(IServerConnection.IFactory.Connect)) <= 2);
    }

    [Fact]
    public async Task Start_StopsTryingWhenTheConnectScreenWasLeft()
    {
        factory.Setup(target => target.Connect(saved, It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("offline"));

        await model.Start();
        ConnectViewModel connect = Assert.IsType<ConnectViewModel>(model.Current);
        factory.Setup(target => target.Connect(saved, It.IsAny<CancellationToken>())).ReturnsAsync(connection.Object);
        connect.Port = "1";
        await Task.Delay(150);

        Assert.IsType<ConnectViewModel>(model.Current);
    }
}
