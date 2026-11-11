namespace Markwardt.ClarityJournal.Tests.Integration;

public sealed class UpdateEndToEndTests : IDisposable
{
    private readonly TempFolder folder = new();

    private readonly FakeClaude claude = new();

    private readonly IServerConnection.IFactory factory = new IServerConnection.Factory(new IMessageChannel.Factory(), Mock.Of<ILog>());

    private readonly List<IServerConnection> opened = [];

    private readonly List<ProcessStartInfo> started = [];

    private readonly CancellationTokenSource restarting = new();

    private readonly byte[] newBuild = Encoding.UTF8.GetBytes("the new server build");

    private readonly string executable;

    private FakeGitHub github = null!;

    private ExecutableInstaller installer = null!;

    private ServerRestart restart = null!;

    public UpdateEndToEndTests()
    {
        executable = folder.Combine("server");
        File.WriteAllText(executable, "the old server build");
    }

    public void Dispose()
    {
        restarting.Dispose();
        folder.Dispose();
    }

    private Mock<IAppInfo> Info(string version)
    {
        Mock<IAppInfo> info = new();
        info.SetupGet(target => target.Name).Returns("Clarity Journal");
        info.SetupGet(target => target.Version).Returns(version);
        return info;
    }

    private ServerUpdater Updater(string serverVersion, string releaseVersion)
    {
        github = new FakeGitHub(releaseVersion, new Dictionary<string, byte[]> { ["ClarityJournal-Server-linux"] = newBuild, ["ClarityJournal-Server-win.exe"] = newBuild, ["ClarityJournal.apk"] = [9, 9, 9] });
        ReleaseFeed feed = github.Feed();
        installer = new ExecutableInstaller(feed, executable, false);
        restart = new ServerRestart(restarting, TextWriter.Null, installer, ["config.json"], _ => null, info =>
        {
            started.Add(info);
            return null;
        }, TimeSpan.FromMilliseconds(20));
        return new ServerUpdater(Info(serverVersion).Object, feed, installer, restart);
    }

    private async Task<IServerConnection> Connect(TestServer server)
    {
        IServerConnection connection = await factory.Connect(server.Settings(), CancellationToken.None);
        opened.Add(connection);
        return connection;
    }

    private async Task Disposed()
    {
        foreach (IServerConnection connection in opened)
        {
            await connection.DisposeAsync();
        }
    }

    private async Task<bool> Until(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 600 && !condition(); attempt++)
        {
            await Task.Delay(20);
        }

        return condition();
    }

    [Fact]
    public async Task ServerVersion_IsTheOneTheServerReportsWhenTheConnectionOpens()
    {
        await using TestServer server = await TestServer.Start(claude, version: "3.2.1");
        IServerConnection connection = await Connect(server);

        Assert.Equal("3.2.1", connection.ServerVersion);
        await Disposed();
    }

    [Fact]
    public async Task UpdateServer_DownloadsTheReleaseStagesItAndRestartsTheServerWhichThenInstallsAndStartsIt()
    {
        ServerUpdater updater = Updater("1.0.0", "1.1.0");
        await using TestServer server = await TestServer.Start(claude, updater: updater, version: "1.0.0");
        IServerConnection connection = await Connect(server);

        await connection.UpdateServer(CancellationToken.None);

        Assert.Equal(newBuild, await File.ReadAllBytesAsync($"{executable}.new"));
        Assert.Equal("the old server build", await File.ReadAllTextAsync(executable));
        Assert.True(restart.IsRequested);
        Assert.True(await Until(() => restarting.IsCancellationRequested));
        Assert.Single(github.Requested, address => address.Contains("/download/", StringComparison.Ordinal));
        Assert.Contains(github.Requested, address => address.EndsWith(OperatingSystem.IsWindows() ? "ClarityJournal-Server-win.exe" : "ClarityJournal-Server-linux", StringComparison.Ordinal));

        int code = restart.Relaunch();

        Assert.Equal(0, code);
        Assert.Equal(newBuild, await File.ReadAllBytesAsync(executable));
        Assert.False(File.Exists($"{executable}.new"));
        ProcessStartInfo relaunch = Assert.Single(started, info => info.FileName == executable);
        Assert.Equal(["config.json"], relaunch.ArgumentList);
        await Disposed();
    }

    [Fact]
    public async Task UpdateServer_WhenTheServerIsAlreadyTheNewestReportsItAndChangesNothing()
    {
        ServerUpdater updater = Updater("1.1.0", "1.1.0");
        await using TestServer server = await TestServer.Start(claude, updater: updater, version: "1.1.0");
        IServerConnection connection = await Connect(server);

        RemoteException exception = await Assert.ThrowsAsync<RemoteException>(() => connection.UpdateServer(CancellationToken.None));

        Assert.Equal("The server is up to date at 1.1.0.", exception.Message);
        Assert.False(File.Exists($"{executable}.new"));
        Assert.False(restart.IsRequested);
        Assert.NotNull(await connection.GetChat(CancellationToken.None));
        await Disposed();
    }

    [Fact]
    public async Task UpdateServer_WhenGitHubFailsTheAppIsToldAndTheServerKeepsRunning()
    {
        ServerUpdater updater = Updater("1.0.0", "1.1.0");
        github.LatestStatus = HttpStatusCode.InternalServerError;
        await using TestServer server = await TestServer.Start(claude, updater: updater, version: "1.0.0");
        IServerConnection connection = await Connect(server);

        RemoteException exception = await Assert.ThrowsAsync<RemoteException>(() => connection.UpdateServer(CancellationToken.None));

        Assert.Contains("GitHub answered 500", exception.Message, StringComparison.Ordinal);
        Assert.False(restart.IsRequested);
        Assert.NotNull(await connection.GetChat(CancellationToken.None));
        await Disposed();
    }

    [Fact]
    public async Task UpdateServer_ADownloadThatDoesNotMatchTheReleasesChecksumIsRefusedAndLeavesNothingBehind()
    {
        ServerUpdater updater = Updater("1.0.0", "1.1.0");
        github.CorruptDownloads = true;
        await using TestServer server = await TestServer.Start(claude, updater: updater, version: "1.0.0");
        IServerConnection connection = await Connect(server);

        RemoteException exception = await Assert.ThrowsAsync<RemoteException>(() => connection.UpdateServer(CancellationToken.None));

        Assert.Contains("checksum", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists($"{executable}.new"));
        Assert.Equal("the old server build", await File.ReadAllTextAsync(executable));
        Assert.False(restart.IsRequested);
        await Disposed();
    }

    [Fact]
    public async Task UpdateServer_ASecondUpdateAfterTheFirstWasAcceptedIsRefused()
    {
        ServerUpdater updater = Updater("1.0.0", "1.1.0");
        await using TestServer server = await TestServer.Start(claude, updater: updater, version: "1.0.0");
        IServerConnection connection = await Connect(server);
        await connection.UpdateServer(CancellationToken.None);

        RemoteException exception = await Assert.ThrowsAsync<RemoteException>(() => connection.UpdateServer(CancellationToken.None));

        Assert.Contains("already restarting", exception.Message, StringComparison.Ordinal);
        await Disposed();
    }

    [Fact]
    public async Task AServerOnlyUpdate_IsSeenByTheAppAsAConnectionThatComesBackAsTheNewVersion()
    {
        TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        string data = folder.Combine("data");
        ServerUpdater updater = Updater("1.0.0", "1.1.0");
        TestServer first = await TestServer.Start(claude, updater: updater, dataFolder: data, configure: config => config with { Port = port }, version: "1.0.0");
        IServerConnection connection = await Connect(first);
        Mock<IAppInfo> app = Info("1.1.0");
        using UpdateViewModel update = new(app.Object, connection, github.Feed(), null, null, TimeSpan.FromMinutes(5));
        await update.Check();
        Assert.Equal("1.0.0", connection.ServerVersion);
        Assert.True(update.IsAvailable);
        Assert.False(update.ClientNeedsUpdate);

        update.Begin();
        update.ConfirmCommand.Execute(null);
        Assert.True(await Until(() => update.Phase == UpdatePhase.Waiting));
        Assert.True(restart.IsRequested);
        await first.DisposeAsync();

        await using TestServer second = await TestServer.Start(claude, dataFolder: data, configure: config => config with { Port = port }, version: "1.1.0");

        Assert.True(await Until(() => update.Phase == UpdatePhase.Idle));
        Assert.Equal("1.1.0", connection.ServerVersion);
        Assert.True(await Until(() => !update.IsAvailable));
        Assert.False(update.IsAvailable);
        await Disposed();
    }
}
