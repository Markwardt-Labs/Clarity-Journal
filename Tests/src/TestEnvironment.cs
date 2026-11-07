namespace Markwardt.ClarityJournal.Tests;

/// <summary>
/// Keeps every test away from real data. The folders must exist, because .NET reports an application data folder that does not exist as an empty path. The server and the client both derive their default folders from the user's application data folder, which is where an installed server keeps the journal, so before any test code runs the test process is pointed at a throwaway folder instead. A config or client built without an explicit folder then lands in the sandbox rather than in the real journal.
/// </summary>
public static class TestEnvironment
{
    /// <summary>
    /// Gets the folder that stands in for the user's application data folders for the whole test run.
    /// </summary>
    public static string Sandbox { get; } = Path.Combine(Path.GetTempPath(), "clarityjournal-test-sandbox-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Redirect()
    {
        Directory.CreateDirectory(Sandbox);

        foreach (string name in new[] { "XDG_DATA_HOME", "XDG_CONFIG_HOME", "XDG_CACHE_HOME", "LOCALAPPDATA", "APPDATA" })
        {
            string folder = Path.Combine(Sandbox, name);
            Directory.CreateDirectory(folder);
            Environment.SetEnvironmentVariable(name, folder);
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(Sandbox, true);
            }
            catch (IOException)
            {
            }
        };
    }
}
