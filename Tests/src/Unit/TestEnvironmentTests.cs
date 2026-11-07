namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class TestEnvironmentTests
{
    private readonly string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void ApplicationDataFolders_AreRedirectedIntoTheSandbox()
    {
        Assert.StartsWith(TestEnvironment.Sandbox, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.Ordinal);
        Assert.StartsWith(TestEnvironment.Sandbox, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), StringComparison.Ordinal);
    }

    [Fact]
    public void AServerConfigWithoutAFolder_CannotReachTheRealJournal()
    {
        ServerConfig config = new() { Key = "k" };

        Assert.StartsWith(TestEnvironment.Sandbox, config.DataFolder, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.Combine(home, ".local", "share"), config.DataFolder, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSandbox_IsNotInsideTheRealApplicationDataFolders()
    {
        Assert.DoesNotContain(Path.Combine(home, ".local", "share"), TestEnvironment.Sandbox, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.Combine(home, ".config"), TestEnvironment.Sandbox, StringComparison.Ordinal);
    }
}
