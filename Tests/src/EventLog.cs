namespace Markwardt.ClarityJournal.Tests;

internal sealed class EventLog
{
    private readonly List<RemoteEvent> events = [];

    public EventLog(IServerConnection connection) => connection.EventReceived += remoteEvent =>
    {
        lock (events)
        {
            events.Add(remoteEvent);
        }
    };

    public List<RemoteEvent> All
    {
        get
        {
            lock (events)
            {
                return [.. events];
            }
        }
    }

    public async Task<RemoteEvent> Next(Func<RemoteEvent, bool> match)
    {
        for (int attempt = 0; attempt < 500; attempt++)
        {
            if (All.FirstOrDefault(match) is { } found)
            {
                return found;
            }

            await Task.Delay(20);
        }

        Assert.Fail("Timed out waiting for an event; received: " + string.Join(", ", All.Select(remoteEvent => $"{remoteEvent.Kind}:{remoteEvent.Working}")));
        return null!;
    }
}
