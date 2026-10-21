namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// Deletes old chat messages once a day.
/// </summary>
internal interface IChatRetention
{
    /// <summary>
    /// Deletes the messages older than the configured number of days at once, and again after every local midnight, until cancelled.
    /// </summary>
    /// <param name="config">The server settings.</param>
    /// <param name="cancellation">Stops the loop.</param>
    /// <returns>A task that completes when cancelled.</returns>
    Task Run(ServerConfig config, CancellationToken cancellation);
}

/// <inheritdoc />
internal sealed class ChatRetention(IChatService chat, TimeProvider time, ILog log) : IChatRetention
{
    /// <inheritdoc />
    public async Task Run(ServerConfig config, CancellationToken cancellation)
    {
        try
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();

                try
                {
                    int removed = await chat.Trim(time.GetUtcNow() - TimeSpan.FromDays(config.ChatRetentionDays), cancellation);
                    log.Write(removed > 0 ? $"Deleted {removed} chat messages older than {config.ChatRetentionDays} days" : "No old chat messages to delete");
                }
                catch (Exception exception) when (exception is SqliteException or IOException)
                {
                    log.Write($"Deleting old chat messages failed: {exception.Message}");
                }

                DateTime now = time.GetLocalNow().DateTime;
                await Task.Delay(now.Date.AddDays(1) - now, time, cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }
}
