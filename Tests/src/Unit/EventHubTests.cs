namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class EventHubTests
{
    [Fact]
    public void Publish_ReachesEverySubscriberUntilItIsDisposed()
    {
        EventHub hub = new();
        List<RemoteEventKind> first = [];
        List<RemoteEventKind> second = [];
        IDisposable subscription = hub.Subscribe(remoteEvent => first.Add(remoteEvent.Kind));
        hub.Subscribe(remoteEvent => second.Add(remoteEvent.Kind));

        hub.Publish(new RemoteEvent { Kind = RemoteEventKind.SummariesChanged });
        subscription.Dispose();
        hub.Publish(new RemoteEvent { Kind = RemoteEventKind.ChatCleared });

        Assert.Equal([RemoteEventKind.SummariesChanged], first);
        Assert.Equal([RemoteEventKind.SummariesChanged, RemoteEventKind.ChatCleared], second);
    }
}
