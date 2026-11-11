namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class ServerUpdaterTests
{
    private readonly Mock<IAppInfo> info = new();

    private readonly Mock<IReleaseFeed> feed = new();

    private readonly Mock<IExecutableInstaller> installer = new();

    private readonly Mock<IServerRestart> restart = new();

    private readonly ServerUpdater updater;

    public ServerUpdaterTests()
    {
        info.SetupGet(x => x.Version).Returns("1.2.3");
        installer.SetupGet(x => x.Executable).Returns("/opt/server");
        updater = new ServerUpdater(info.Object, feed.Object, installer.Object, restart.Object);
    }

    private static ReleaseInfo Release(string version)
        => new() { Version = AppVersion.TryParse(version, out AppVersion? parsed) ? parsed : throw new ArgumentException(version), Assets = [] };

    private string Asset()
        => OperatingSystem.IsWindows() ? "ClarityJournal-Server-win.exe" : "ClarityJournal-Server-linux";

    [Fact]
    public async Task Update_DownloadsTheNewerReleaseAndRequestsARestart()
    {
        ReleaseInfo newer = Release("1.3.0");
        feed.Setup(x => x.Latest(It.IsAny<CancellationToken>())).ReturnsAsync(newer);

        await updater.Update(CancellationToken.None);

        installer.Verify(x => x.Stage(newer, Asset(), null, It.IsAny<CancellationToken>()), Times.Once);
        restart.Verify(x => x.Request(), Times.Once);
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.2.2")]
    public async Task Update_RefusesWhenThereIsNothingNewer(string latest)
    {
        feed.Setup(x => x.Latest(It.IsAny<CancellationToken>())).ReturnsAsync(Release(latest));

        RequestException exception = await Assert.ThrowsAsync<RequestException>(() => updater.Update(CancellationToken.None));

        Assert.Contains("up to date", exception.Message);
        installer.Verify(x => x.Stage(It.IsAny<ReleaseInfo>(), It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Never);
        restart.Verify(x => x.Request(), Times.Never);
    }

    [Fact]
    public async Task Update_RefusesWhenGitHubHasNoRelease()
    {
        feed.Setup(x => x.Latest(It.IsAny<CancellationToken>())).ReturnsAsync((ReleaseInfo?)null);

        await Assert.ThrowsAsync<RequestException>(() => updater.Update(CancellationToken.None));
    }

    [Fact]
    public async Task Update_RefusesWhenTheServerDoesNotRunFromAnExecutable()
    {
        installer.SetupGet(x => x.Executable).Returns((string?)null);

        RequestException exception = await Assert.ThrowsAsync<RequestException>(() => updater.Update(CancellationToken.None));

        Assert.Contains("installed release", exception.Message);
        feed.Verify(x => x.Latest(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_DoesNotRestartWhenTheDownloadFailsAndSaysWhy()
    {
        feed.Setup(x => x.Latest(It.IsAny<CancellationToken>())).ReturnsAsync(Release("2.0.0"));
        installer.Setup(x => x.Stage(It.IsAny<ReleaseInfo>(), It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("disk full"));

        RequestException exception = await Assert.ThrowsAsync<RequestException>(() => updater.Update(CancellationToken.None));

        Assert.Equal("disk full", exception.Message);
        restart.Verify(x => x.Request(), Times.Never);
    }

    [Fact]
    public async Task Update_DoesNotRestartWhenTheFolderIsNotWritableAndSaysSo()
    {
        feed.Setup(x => x.Latest(It.IsAny<CancellationToken>())).ReturnsAsync(Release("2.0.0"));
        installer.Setup(x => x.Stage(It.IsAny<ReleaseInfo>(), It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException("denied"));

        RequestException exception = await Assert.ThrowsAsync<RequestException>(() => updater.Update(CancellationToken.None));

        Assert.Contains("not writable", exception.Message);
        restart.Verify(x => x.Request(), Times.Never);
    }

    [Fact]
    public async Task Update_WhenGitHubCannotBeReachedSaysSoInsteadOfFailingGenerically()
    {
        feed.Setup(x => x.Latest(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("GitHub did not answer in time."));

        RequestException exception = await Assert.ThrowsAsync<RequestException>(() => updater.Update(CancellationToken.None));

        Assert.Equal("GitHub did not answer in time.", exception.Message);
        restart.Verify(x => x.Request(), Times.Never);
    }

    [Fact]
    public async Task Update_AnAppVersionTheServerCannotReadIsUpdatedRatherThanRefused()
    {
        info.SetupGet(x => x.Version).Returns("not a version");
        feed.Setup(x => x.Latest(It.IsAny<CancellationToken>())).ReturnsAsync(Release("1.0.0"));

        await updater.Update(CancellationToken.None);

        restart.Verify(x => x.Request(), Times.Once);
    }

    [Fact]
    public async Task Update_RefusesASecondUpdateWhileOneRunsAndAfterARestartWasRequested()
    {
        TaskCompletionSource gate = new();
        feed.Setup(x => x.Latest(It.IsAny<CancellationToken>())).ReturnsAsync(Release("2.0.0"));
        installer.Setup(x => x.Stage(It.IsAny<ReleaseInfo>(), It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>())).Returns(gate.Task);

        Task first = updater.Update(CancellationToken.None);
        RequestException running = await Assert.ThrowsAsync<RequestException>(() => updater.Update(CancellationToken.None));
        gate.SetResult();
        await first;
        restart.SetupGet(x => x.IsRequested).Returns(true);
        RequestException restarting = await Assert.ThrowsAsync<RequestException>(() => updater.Update(CancellationToken.None));

        Assert.Contains("already updating", running.Message);
        Assert.Contains("already restarting", restarting.Message);
    }
}
