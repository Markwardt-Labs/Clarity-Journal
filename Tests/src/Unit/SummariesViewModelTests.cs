namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class SummariesViewModelTests
{
    private readonly Mock<IServerConnection> connection = new();

    private readonly SummariesViewModel model;

    public SummariesViewModelTests()
    {
        connection.Setup(target => target.GetSummaries(It.IsAny<SummaryKind>(), It.IsAny<CancellationToken>())).ReturnsAsync((SummaryKind kind, CancellationToken _) => (IReadOnlyList<Summary>)[Summary(kind)]);
        model = new SummariesViewModel(connection.Object);
    }

    private Summary Summary(SummaryKind kind) => new() { Kind = kind, Period = kind.ToString(), Start = new DateOnly(2026, 10, 12), End = new DateOnly(2026, 10, 18), Text = $"{kind} text", EntryCount = 4, UpdatedAt = DateTimeOffset.UnixEpoch };

    [Fact]
    public async Task Load_ShowsTheWeeklySummaries()
    {
        await model.Load();

        Assert.Equal("Week text", Assert.Single(model.Items).Text);
        Assert.Equal(["Week", "Month", "Year", "Life"], model.Kinds.Select(choice => choice.Label));
        Assert.Equal(["Week"], model.Kinds.Where(choice => choice.IsSelected).Select(choice => choice.Label));
    }

    [Theory]
    [InlineData(1, SummaryKind.Month)]
    [InlineData(2, SummaryKind.Year)]
    [InlineData(3, SummaryKind.Lifetime)]
    public async Task SelectingAKind_ShowsItsSummariesAndMarksTheSegment(int index, SummaryKind kind)
    {
        await model.Load();

        model.Kinds[index].Select.Execute(null);

        connection.Verify(target => target.GetSummaries(kind, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal($"{kind} text", Assert.Single(model.Items).Text);
        Assert.Equal([kind], model.Kinds.Where(choice => choice.IsSelected).Select(choice => choice.Kind));
    }

    [Fact]
    public async Task KindWithoutSummaries_ShowsNothing()
    {
        connection.Setup(target => target.GetSummaries(It.IsAny<SummaryKind>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        await model.Load();
        model.Kinds[3].Select.Execute(null);

        Assert.Empty(model.Items);
        Assert.Null(model.Error);
    }

    [Fact]
    public async Task Handle_SummariesChanged_ReadsTheShownKindAgain()
    {
        await model.Load();
        connection.Invocations.Clear();

        await model.Handle(new RemoteEvent { Kind = RemoteEventKind.SummariesChanged });

        connection.Verify(target => target.GetSummaries(SummaryKind.Week, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Failed_ShowsTheError()
    {
        await model.Handle(new RemoteEvent { Kind = RemoteEventKind.Failed, Error = "Summaries could not be updated: no network" });

        Assert.Equal("Summaries could not be updated: no network", model.Error);
    }

    [Theory]
    [InlineData(SummaryKind.Week, "Oct 12 to Oct 18, 2026")]
    [InlineData(SummaryKind.Month, "October 2026")]
    [InlineData(SummaryKind.Year, "Year")]
    [InlineData(SummaryKind.Lifetime, "Life")]
    public void SummaryItem_TitlesThePeriodTheWayPeopleSayIt(SummaryKind kind, string title)
    {
        SummaryItemViewModel item = new(Summary(kind));

        Assert.Equal(title, item.Title);
        Assert.StartsWith("4 entries, updated ", item.Subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public void SummaryItem_SingleEntry_IsSingular()
    {
        Assert.StartsWith("1 entry, ", new SummaryItemViewModel(Summary(SummaryKind.Week) with { EntryCount = 1 }).Subtitle, StringComparison.Ordinal);
    }
}
