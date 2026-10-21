namespace Markwardt.ClarityJournal.Tests;

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => now;
}
