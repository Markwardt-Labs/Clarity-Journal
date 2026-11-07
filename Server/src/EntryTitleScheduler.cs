namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// Writes the missing entry titles when the server starts and again whenever a new day begins.
/// </summary>
internal interface IEntryTitleScheduler
{
    /// <summary>
    /// Writes the missing titles at once, then after every local midnight, until cancelled.
    /// </summary>
    /// <param name="config">The server settings.</param>
    /// <param name="cancellation">Stops the scheduler.</param>
    /// <returns>A task that completes when cancelled.</returns>
    Task Run(ServerConfig config, CancellationToken cancellation);
}

/// <inheritdoc />
internal sealed class EntryTitleScheduler(IEntryTitler titler, TimeProvider time, ILog log) : IEntryTitleScheduler
{
    private readonly TimeSpan retryDelay = TimeSpan.FromHours(1);

    // A timer can fire a few milliseconds early, which would still be the old day and miss the entry of the day that just ended for a whole day more.
    private readonly TimeSpan slack = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public async Task Run(ServerConfig config, CancellationToken cancellation)
    {
        try
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                bool done = await titler.Run(config, cancellation);
                DateTime now = time.GetLocalNow().DateTime;
                TimeSpan untilNextDay = now.Date.AddDays(1) + slack - now;
                TimeSpan wait = done || untilNextDay < retryDelay ? untilNextDay : retryDelay;
                log.Write($"Next check for entries without a title: {now + wait:yyyy-MM-dd HH:mm}");
                await Task.Delay(wait, time, cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }
}
