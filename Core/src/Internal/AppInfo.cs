namespace Markwardt.ClarityJournal;

internal sealed class AppInfo(string version) : IAppInfo
{
    /// <inheritdoc />
    public string Name { get; } = "Clarity Journal";

    /// <inheritdoc />
    public string Version { get; } = version;
}
