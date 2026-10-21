namespace Markwardt.ClarityJournal.Tests;

public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly List<ManualTimer> timers = [];

    private DateTimeOffset now = start;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public int ActiveTimers => timers.Count(timer => !timer.Disposed);

    public override DateTimeOffset GetUtcNow() => now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ManualTimer timer = new(callback, state, now + dueTime, period);
        timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        DateTimeOffset target = now + by;

        while (timers.Where(timer => !timer.Disposed && timer.Next <= target).OrderBy(timer => timer.Next).FirstOrDefault() is { } due)
        {
            now = due.Next;
            due.Fire();
        }

        now = target;
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, DateTimeOffset next, TimeSpan period) : ITimer
    {
        public DateTimeOffset Next { get; private set; } = next;

        public bool Disposed { get; private set; }

        public void Fire()
        {
            Next = period > TimeSpan.Zero ? Next + period : DateTimeOffset.MaxValue;
            callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan newPeriod) => false;

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
