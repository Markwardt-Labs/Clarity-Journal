namespace Markwardt.ClarityJournal;

/// <summary>
/// Reads the releases of the Clarity Journal repository on GitHub and downloads their files.
/// </summary>
public interface IReleaseFeed
{
    /// <summary>
    /// Finds the newest published release, leaving out drafts and pre-releases.
    /// </summary>
    /// <param name="cancellation">Cancels the request.</param>
    /// <returns>The release, or <see langword="null" /> when the repository has none.</returns>
    /// <exception cref="IOException">GitHub could not be reached or answered with something else than a release.</exception>
    Task<ReleaseInfo?> Latest(CancellationToken cancellation = default);

    /// <summary>
    /// Downloads a release file and checks it against its size and SHA-256.
    /// </summary>
    /// <param name="asset">The file to download.</param>
    /// <param name="path">Where to write it; a file that is already there is replaced.</param>
    /// <param name="progress">Receives the fraction downloaded, from 0 to 1, or <see langword="null" /> to ignore it.</param>
    /// <param name="cancellation">Cancels the download.</param>
    /// <exception cref="IOException">The download failed or the file does not match what the release states; nothing is left at <paramref name="path" />.</exception>
    Task Download(ReleaseAsset asset, string path, IProgress<double>? progress, CancellationToken cancellation = default);

    /// <summary>
    /// Creates <see cref="IReleaseFeed" /> instances.
    /// </summary>
    public interface IFactory
    {
        /// <summary>
        /// Creates the feed of the Clarity Journal repository.
        /// </summary>
        /// <returns>The feed.</returns>
        IReleaseFeed Create();
    }

    /// <inheritdoc />
    public sealed class Factory : IFactory
    {
        /// <inheritdoc />
        public IReleaseFeed Create()
            => new ReleaseFeed(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, "Markwardt-Labs/Clarity-Journal");
    }
}
