namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class JournalServiceTests
{
    private readonly Mock<IEntryStore> entries = new();

    private readonly Mock<IJournalExport> export = new();

    private readonly EventHub events = new();

    private readonly List<RemoteEvent> published = [];

    private readonly DateTimeOffset now = new(2026, 10, 16, 21, 30, 0, TimeSpan.Zero);

    private readonly JournalService service;

    public JournalServiceTests()
    {
        events.Subscribe(published.Add);
        entries.Setup(store => store.Save(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync((DateOnly date, string text, DateTimeOffset at, CancellationToken _) => new JournalEntry { Date = date, Text = text, UpdatedAt = at });
        service = new JournalService(entries.Object, export.Object, events, new FixedTimeProvider(now));
    }

    [Fact]
    public async Task Save_StoresTrimmedTextExportsItAndAnnouncesTheDay()
    {
        JournalEntry entry = await service.Save(new DateOnly(2026, 10, 16), "  A good day.\n", CancellationToken.None);

        Assert.Equal("A good day.", entry.Text);
        Assert.Equal(now, entry.UpdatedAt);
        export.Verify(target => target.WriteEntry(entry, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(new DateOnly(2026, 10, 16), Assert.Single(published).Date);
        Assert.Equal(RemoteEventKind.EntryChanged, published[0].Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    public async Task Save_BlankText_IsRefused(string text)
    {
        await Assert.ThrowsAsync<RequestException>(() => service.Save(new DateOnly(2026, 10, 16), text, CancellationToken.None));

        entries.Verify(store => store.Save(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Save_TooLongText_IsRefused()
    {
        await Assert.ThrowsAsync<RequestException>(() => service.Save(new DateOnly(2026, 10, 16), new string('a', 100001), CancellationToken.None));
    }

    [Fact]
    public async Task Save_AllowsTomorrowForTimeZonesButNotLater()
    {
        await service.Save(new DateOnly(2026, 10, 17), "ahead of the server", CancellationToken.None);

        await Assert.ThrowsAsync<RequestException>(() => service.Save(new DateOnly(2026, 10, 18), "too far", CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesTheEntryAndItsFileAndAnnouncesTheDay()
    {
        entries.Setup(store => store.Delete(new DateOnly(2026, 10, 16), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await service.Delete(new DateOnly(2026, 10, 16), CancellationToken.None);

        export.Verify(target => target.DeleteEntry(new DateOnly(2026, 10, 16)), Times.Once);
        Assert.Equal(RemoteEventKind.EntryChanged, Assert.Single(published).Kind);
    }

    [Fact]
    public async Task Delete_DayWithoutEntry_IsRefused()
    {
        entries.Setup(store => store.Delete(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<RequestException>(() => service.Delete(new DateOnly(2026, 10, 16), CancellationToken.None));

        Assert.Empty(published);
    }

    [Fact]
    public async Task ListAndGet_ReadFromTheStore()
    {
        JournalEntry entry = new() { Date = new DateOnly(2026, 10, 16), Text = "x", UpdatedAt = now };
        entries.Setup(store => store.Get(new DateOnly(2026, 10, 16), It.IsAny<CancellationToken>())).ReturnsAsync(entry);
        entries.Setup(store => store.List(new DateOnly(2026, 10, 1), null, It.IsAny<CancellationToken>())).ReturnsAsync([entry]);

        Assert.Same(entry, await service.Get(new DateOnly(2026, 10, 16), CancellationToken.None));
        Assert.Equal([entry], await service.List(new DateOnly(2026, 10, 1), null, CancellationToken.None));
    }
}
