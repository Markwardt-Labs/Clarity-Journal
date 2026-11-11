namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class UpdateViewModelTests
{
    private readonly Mock<IAppInfo> info = new();

    private readonly Mock<IServerConnection> connection = new();

    private readonly Mock<IReleaseFeed> feed = new();

    private readonly Mock<IClientUpdater> updater = new();

    private string? serverVersion = "1.0.0";

    public UpdateViewModelTests()
    {
        info.SetupGet(target => target.Version).Returns("1.0.0");
        connection.SetupGet(target => target.ServerVersion).Returns(() => serverVersion);
        updater.SetupGet(target => target.IsSupported).Returns(true);
        Latest("1.1.0");
    }

    private async Task WaitFor(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            await Task.Delay(20);
        }
    }

    private void Latest(string? version)
        => feed.Setup(target => target.Latest(It.IsAny<CancellationToken>())).ReturnsAsync(version is null ? null : new ReleaseInfo { Version = AppVersion.TryParse(version, out AppVersion? parsed) ? parsed : throw new ArgumentException(version), Assets = [] });

    private UpdateViewModel Create(IReleaseFeed? source = null, IClientUpdater? installer = null, Func<Task>? prepare = null)
        => new(info.Object, connection.Object, source ?? feed.Object, installer ?? updater.Object, prepare, TimeSpan.FromMilliseconds(30));

    private void InstallIsNeverCalled()
        => updater.Verify(target => target.Install(It.IsAny<ReleaseInfo>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task Check_OffersAnUpdateWhenTheAppAndTheServerAreOlder()
    {
        using UpdateViewModel update = Create();

        await update.Check();

        Assert.True(update.ClientNeedsUpdate);
        Assert.True(update.ServerNeedsUpdate);
        Assert.True(update.IsAvailable);
        Assert.Equal("Version 1.1.0 replaces 1.0.0 in this app and 1.0.0 on the server.", update.Summary);
        Assert.Equal("Update to 1.1.0", update.ButtonText);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.9.0")]
    public async Task Check_OffersNothingWhenNothingIsOlderThanTheLatestRelease(string latest)
    {
        Latest(latest);
        using UpdateViewModel update = Create();

        await update.Check();

        Assert.False(update.IsAvailable);
    }

    [Fact]
    public async Task Check_OffersOnlyTheServerWhenTheAppIsCurrentOrCannotUpdateItself()
    {
        info.SetupGet(target => target.Version).Returns("1.1.0");
        using UpdateViewModel current = Create();
        await current.Check();

        Assert.False(current.ClientNeedsUpdate);
        Assert.True(current.ServerNeedsUpdate);
        Assert.Equal("Version 1.1.0 replaces 1.0.0 on the server.", current.Summary);

        info.SetupGet(target => target.Version).Returns("1.0.0");
        updater.SetupGet(target => target.IsSupported).Returns(false);
        using UpdateViewModel unsupported = Create();
        await unsupported.Check();

        Assert.False(unsupported.ClientNeedsUpdate);
        Assert.True(unsupported.IsAvailable);
    }

    [Fact]
    public async Task Check_OffersOnlyTheAppWhenTheServerIsCurrent()
    {
        serverVersion = "1.1.0";
        using UpdateViewModel update = Create();

        await update.Check();

        Assert.True(update.ClientNeedsUpdate);
        Assert.False(update.ServerNeedsUpdate);
        Assert.Equal("Version 1.1.0 replaces 1.0.0 in this app.", update.Summary);
    }

    [Fact]
    public async Task Check_TreatsAServerThatSaysNoVersionAsOlder()
    {
        serverVersion = null;
        info.SetupGet(target => target.Version).Returns("1.1.0");
        using UpdateViewModel update = Create();

        await update.Check();

        Assert.True(update.ServerNeedsUpdate);
        Assert.Contains("the older version", update.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_APreReleaseIsEarlierThanItsFinalRelease()
    {
        info.SetupGet(target => target.Version).Returns("1.1.0-beta.1");
        serverVersion = "1.1.0";
        using UpdateViewModel update = Create();

        await update.Check();

        Assert.True(update.ClientNeedsUpdate);
        Assert.False(update.ServerNeedsUpdate);
    }

    [Fact]
    public async Task Check_WithNoReleaseOffersNothing()
    {
        Latest(null);
        using UpdateViewModel update = Create();

        await update.Check();

        Assert.False(update.IsAvailable);
        Assert.Equal("The app and its server are up to date.", update.Summary);
        Assert.Equal("Update", update.ButtonText);
    }

    [Fact]
    public async Task Check_AFailedLookupIsSilentAndKeepsWhatWasFoundBefore()
    {
        using UpdateViewModel update = Create();
        await update.Check();
        feed.Setup(target => target.Latest(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("offline"));

        await update.Check();

        Assert.True(update.IsAvailable);
        Assert.Equal("1.1.0", update.Latest!.Version.ToString());
        Assert.Null(update.Error);
    }

    [Fact]
    public async Task Check_DoesNothingWithoutAFeed()
    {
        using UpdateViewModel update = new(info.Object, connection.Object, null, updater.Object);

        await update.Check();

        Assert.False(update.IsAvailable);
    }

    [Fact]
    public async Task Begin_ShowsTheConfirmationOnlyWhenAnUpdateIsAvailable()
    {
        using UpdateViewModel update = Create();
        update.Begin();

        Assert.False(update.IsOpen);

        await update.Check();
        update.BeginCommand.Execute(null);

        Assert.True(update.IsOpen);
        Assert.Equal("Cancel", update.CloseText);

        update.CloseCommand.Execute(null);

        Assert.False(update.IsOpen);
        InstallIsNeverCalled();
        connection.Verify(target => target.UpdateServer(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Begin_WarnsAboutTheServersRestartOnlyWhenTheServerIsUpdated()
    {
        using UpdateViewModel both = Create();
        await both.Check();
        both.Begin();

        Assert.True(both.HasWarning);
        Assert.Contains("restarts", both.Warning, StringComparison.Ordinal);

        serverVersion = "1.1.0";
        using UpdateViewModel appOnly = Create();
        await appOnly.Check();
        appOnly.Begin();

        Assert.False(appOnly.HasWarning);
    }

    [Fact]
    public async Task Confirm_UpdatesTheServerFirstAndThenTheApp()
    {
        List<string> order = [];
        connection.Setup(target => target.UpdateServer(It.IsAny<CancellationToken>())).Returns(() =>
        {
            order.Add("server");
            return Task.CompletedTask;
        });
        updater.Setup(target => target.Install(It.IsAny<ReleaseInfo>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>())).Returns(() =>
        {
            order.Add("app");
            return Task.CompletedTask;
        });
        using UpdateViewModel update = Create();
        await update.Check();
        update.Begin();

        update.ConfirmCommand.Execute(null);
        await WaitFor(() => update.Phase == UpdatePhase.Finished);

        Assert.Equal(["server", "app"], order);
        Assert.True(update.CanClose);
        Assert.Equal("Close", update.CloseText);
        updater.Verify(target => target.Install(It.Is<ReleaseInfo>(release => release.Version.ToString() == "1.1.0"), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Confirm_SavesTheEntryBeforeAnythingIsRestartedOrReplaced()
    {
        List<string> order = [];
        connection.Setup(target => target.UpdateServer(It.IsAny<CancellationToken>())).Returns(() =>
        {
            order.Add("server");
            return Task.CompletedTask;
        });
        using UpdateViewModel update = Create(prepare: () =>
        {
            order.Add("saved");
            return Task.CompletedTask;
        });
        await update.Check();
        update.Begin();

        update.ConfirmCommand.Execute(null);
        await WaitFor(() => update.Phase == UpdatePhase.Finished);

        Assert.Equal(["saved", "server"], order);
    }

    [Fact]
    public async Task Confirm_WhenTheEntryCannotBeSavedNothingIsUpdatedAndTheReasonIsShown()
    {
        using UpdateViewModel update = Create(prepare: () => throw new InvalidOperationException("Today's entry could not be saved, so the update did not start."));
        await update.Check();
        update.Begin();

        update.ConfirmCommand.Execute(null);
        await WaitFor(() => update.HasError);

        Assert.Equal("Today's entry could not be saved, so the update did not start.", update.Error);
        Assert.Equal(UpdatePhase.Confirming, update.Phase);
        connection.Verify(target => target.UpdateServer(It.IsAny<CancellationToken>()), Times.Never);
        InstallIsNeverCalled();
    }

    [Fact]
    public async Task Confirm_ForTheServerOnlyWaitsForItToComeBackAndThenCloses()
    {
        info.SetupGet(target => target.Version).Returns("1.1.0");
        using UpdateViewModel update = Create();
        await update.Check();
        update.Begin();

        update.ConfirmCommand.Execute(null);
        await WaitFor(() => update.Phase == UpdatePhase.Waiting);

        Assert.True(update.IsWorking);
        Assert.True(update.IsUpdating);
        Assert.False(update.IsInstalling);
        Assert.True(update.CanClose);
        InstallIsNeverCalled();

        serverVersion = "1.1.0";
        connection.Raise(target => target.Reconnected += null);

        Assert.Equal(UpdatePhase.Idle, update.Phase);
        Assert.False(update.IsUpdating);
        await WaitFor(() => !update.IsAvailable);
        Assert.False(update.IsAvailable);
    }

    [Fact]
    public async Task Confirm_ShowsTheRefusalOfTheServerAndInstallsNothing()
    {
        connection.Setup(target => target.UpdateServer(It.IsAny<CancellationToken>())).ThrowsAsync(new RemoteException("The server is up to date."));
        using UpdateViewModel update = Create();
        await update.Check();
        update.Begin();

        update.ConfirmCommand.Execute(null);
        await WaitFor(() => update.HasError);

        Assert.Equal("The server is up to date.", update.Error);
        Assert.Equal(UpdatePhase.Confirming, update.Phase);
        InstallIsNeverCalled();
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(RemoteException))]
    public async Task Confirm_ExplainsThatAServerWithoutAVersionIsTooOldToUpdateItself(Type failure)
    {
        serverVersion = null;
        info.SetupGet(target => target.Version).Returns("1.1.0");
        connection.Setup(target => target.UpdateServer(It.IsAny<CancellationToken>())).ThrowsAsync((Exception)Activator.CreateInstance(failure, "closed")!);
        using UpdateViewModel update = Create();
        await update.Check();
        update.Begin();

        update.ConfirmCommand.Execute(null);
        await WaitFor(() => update.HasError);

        Assert.Contains("too old to update itself (closed)", update.Error, StringComparison.Ordinal);
        Assert.Equal(UpdatePhase.Confirming, update.Phase);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task Confirm_ShowsAFailedInstallationAndAllowsTryingAgain(Type failure)
    {
        serverVersion = "1.1.0";
        updater.Setup(target => target.Install(It.IsAny<ReleaseInfo>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>())).ThrowsAsync((Exception)Activator.CreateInstance(failure, "broken")!);
        using UpdateViewModel update = Create();
        await update.Check();
        update.Begin();

        update.ConfirmCommand.Execute(null);
        await WaitFor(() => update.HasError);

        Assert.Equal("broken", update.Error);
        Assert.Equal(UpdatePhase.Confirming, update.Phase);
        Assert.True(update.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task Confirm_ReportsTheDownloadProgressAndCannotBeLeftWhileItRuns()
    {
        serverVersion = "1.1.0";
        TaskCompletionSource gate = new();
        updater.Setup(target => target.Install(It.IsAny<ReleaseInfo>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>())).Returns(async (ReleaseInfo _, IProgress<double> progress, CancellationToken _) =>
        {
            progress.Report(0.5);
            await gate.Task;
        });
        using UpdateViewModel update = Create();
        await update.Check();
        update.Begin();
        Assert.True(update.IsProgressUnknown);

        update.ConfirmCommand.Execute(null);
        await WaitFor(() => update.Progress > 0);

        Assert.Equal(0.5, update.Progress);
        Assert.False(update.IsProgressUnknown);
        Assert.True(update.IsInstalling);
        Assert.False(update.CanClose);
        Assert.Contains("1.1.0", update.Status, StringComparison.Ordinal);

        update.CloseCommand.Execute(null);
        Assert.True(update.IsUpdating);

        gate.SetResult();
        await WaitFor(() => update.Phase == UpdatePhase.Finished);
        Assert.False(update.IsInstalling);
    }

    [Fact]
    public async Task Confirm_WhenNotOpenedByTheConfirmationDoesNothing()
    {
        using UpdateViewModel update = Create();
        await update.Check();

        update.ConfirmCommand.Execute(null);
        await Task.Delay(50);

        Assert.Equal(UpdatePhase.Idle, update.Phase);
        connection.Verify(target => target.UpdateServer(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Watch_LooksAgainAtIntervalsUntilDisposed()
    {
        int lookups = 0;
        feed.Setup(target => target.Latest(It.IsAny<CancellationToken>())).Returns(() =>
        {
            Interlocked.Increment(ref lookups);
            return Task.FromResult<ReleaseInfo?>(null);
        });
        UpdateViewModel update = Create();

        Task watching = update.Watch();
        await WaitFor(() => Volatile.Read(ref lookups) >= 3);
        update.Dispose();
        await watching;
        int after = Volatile.Read(ref lookups);
        await Task.Delay(100);

        Assert.True(after >= 3);
        Assert.Equal(after, Volatile.Read(ref lookups));
    }

    [Fact]
    public async Task Watch_FindsAReleaseThatWasPublishedAfterTheFirstLookup()
    {
        Latest("1.0.0");
        UpdateViewModel update = Create();
        Task watching = update.Watch();
        await WaitFor(() => update.Latest is not null);
        Assert.False(update.IsAvailable);

        Latest("1.2.0");
        await WaitFor(() => update.IsAvailable);
        update.Dispose();
        await watching;

        Assert.Equal("1.2.0", update.Latest!.Version.ToString());
    }

    [Fact]
    public async Task Check_AfterDisposeDoesNothingAndDoesNotThrow()
    {
        UpdateViewModel update = Create();
        update.Dispose();

        await update.Check();
        await update.Watch();

        Assert.False(update.IsAvailable);
    }

    [Fact]
    public async Task Reconnecting_LooksForANewerReleaseAgainAndOffersIt()
    {
        Latest("1.0.0");
        using UpdateViewModel update = Create();
        await update.Check();
        Assert.False(update.IsAvailable);

        Latest("1.1.0");
        connection.Raise(target => target.Reconnected += null);
        await WaitFor(() => update.IsAvailable);

        Assert.True(update.IsAvailable);
        Assert.Equal("1.1.0", update.Latest!.Version.ToString());
    }

    [Fact]
    public async Task Reconnecting_ReadsTheServerVersionAgain()
    {
        using UpdateViewModel update = Create();
        await update.Check();
        List<string?> raised = [];
        update.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        serverVersion = "1.1.0";
        connection.Raise(target => target.Reconnected += null);

        Assert.Contains(nameof(UpdateViewModel.ServerNeedsUpdate), raised);
        Assert.False(update.ServerNeedsUpdate);
    }

    [Fact]
    public async Task Dispose_StopsListeningToTheConnection()
    {
        UpdateViewModel update = Create();
        await update.Check();
        update.Dispose();
        List<string?> raised = [];
        update.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        connection.Raise(target => target.Reconnected += null);

        Assert.Empty(raised);
    }
}
