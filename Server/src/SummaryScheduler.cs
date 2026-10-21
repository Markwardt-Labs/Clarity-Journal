namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// Runs the summary update every Friday, and straight away when the server was off during the last Friday run.
/// </summary>
internal interface ISummaryScheduler
{
    /// <summary>
    /// Waits for each run and starts it, until cancelled.
    /// </summary>
    /// <param name="config">The server settings.</param>
    /// <param name="cancellation">Stops the scheduler.</param>
    /// <returns>A task that completes when cancelled.</returns>
    Task Run(ServerConfig config, CancellationToken cancellation);
}

/// <inheritdoc />
internal sealed class SummaryScheduler(ISummarizer summarizer, ISummaryStore summaries, ISummarySchedule schedule, TimeProvider time, ILog log) : ISummaryScheduler
{
    private readonly TimeSpan retryDelay = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public async Task Run(ServerConfig config, CancellationToken cancellation)
    {
        try
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                DateTime now = time.GetLocalNow().DateTime;
                DateTimeOffset? last = await summaries.GetLastRun(cancellation);
                bool missed = last is null || TimeZoneInfo.ConvertTime(last.Value, time.LocalTimeZone).DateTime < schedule.Previous(now, config.SummaryTime);
                DateTime due = missed ? now : schedule.Next(now, config.SummaryTime);

                if (due > now)
                {
                    log.Write($"Next summary update: {due:yyyy-MM-dd HH:mm}");
                    await Task.Delay(due - now, time, cancellation);
                }

                if (!await summarizer.Run(config, cancellation))
                {
                    await Task.Delay(retryDelay, time, cancellation);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }
}
