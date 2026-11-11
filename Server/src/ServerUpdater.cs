namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// Updates the server to the newest release on GitHub.
/// </summary>
internal interface IServerUpdater
{
    /// <summary>
    /// Downloads the newest release next to the running executable and requests a restart, which puts it in place once the server has shut down.
    /// </summary>
    /// <param name="cancellation">Cancels the download.</param>
    /// <returns>A task that completes once the new executable is downloaded; the restart follows shortly after.</returns>
    /// <exception cref="RequestException">There is no newer release, there is no build for this platform, the server does not run from an installed release, an update is already running, GitHub could not be reached, the download failed or the folder of the executable is not writable; the message says which, since the app shows it.</exception>
    Task Update(CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class ServerUpdater(IAppInfo info, IReleaseFeed feed, IExecutableInstaller installer, IServerRestart restart) : IServerUpdater
{
    private readonly SemaphoreSlim updating = new(1, 1);

    /// <inheritdoc />
    public async Task Update(CancellationToken cancellation = default)
    {
        if (!await updating.WaitAsync(0, cancellation))
        {
            throw new RequestException("The server is already updating.");
        }

        try
        {
            if (restart.IsRequested)
            {
                throw new RequestException("The server is already restarting with an update.");
            }

            if (installer.Executable is null)
            {
                throw new RequestException("The server does not run from an installed release, so it cannot update itself.");
            }

            string asset = AssetName();
            ReleaseInfo release = await Latest(cancellation);

            if (AppVersion.TryParse(info.Version, out AppVersion? current) && !(release.Version > current))
            {
                throw new RequestException($"The server is up to date at {info.Version}.");
            }

            try
            {
                await installer.Stage(release, asset, null, cancellation);
            }
            catch (IOException exception)
            {
                throw new RequestException(exception.Message);
            }
            catch (UnauthorizedAccessException)
            {
                throw new RequestException("The folder of the server is not writable, so it cannot update itself.");
            }

            restart.Request();
        }
        finally
        {
            updating.Release();
        }
    }

    private async Task<ReleaseInfo> Latest(CancellationToken cancellation = default)
    {
        try
        {
            return await feed.Latest(cancellation) ?? throw new RequestException("No release was found on GitHub.");
        }
        catch (IOException exception)
        {
            throw new RequestException(exception.Message);
        }
    }

    private string AssetName()
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 || !(OperatingSystem.IsWindows() || OperatingSystem.IsLinux()))
        {
            throw new RequestException("There is no release build of the server for this platform.");
        }

        return OperatingSystem.IsWindows() ? "ClarityJournal-Server-win.exe" : "ClarityJournal-Server-linux";
    }
}
