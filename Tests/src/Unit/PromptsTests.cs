namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class PromptsTests
{
    private readonly Prompts prompts = new();

    private JournalEntry Entry(string date, string text) => new() { Date = DateOnly.Parse(date, CultureInfo.InvariantCulture), Text = text, UpdatedAt = DateTimeOffset.UnixEpoch };

    [Fact]
    public void Summary_WithoutPrevious_SaysItIsTheFirstAndListsTheEntriesWithTheirWeekday()
    {
        string prompt = prompts.Summary(SummaryKind.Week.PeriodOf(new DateOnly(2026, 10, 16)), null, [Entry("2026-10-16", "Went for a run.")]);

        Assert.Contains("the week of 2026-10-12 to 2026-10-18", prompt);
        Assert.Contains("(none yet, this is the first summary)", prompt);
        Assert.Contains("### 2026-10-16 (Friday)", prompt);
        Assert.Contains("Went for a run.", prompt);
    }

    [Fact]
    public void Summary_WithPrevious_IncludesItBeforeTheNewEntries()
    {
        string prompt = prompts.Summary(SummaryKind.Month.PeriodOf(new DateOnly(2026, 10, 16)), "You started running.", [Entry("2026-10-19", "Ran again.")]);

        Assert.Contains("the month 2026-10", prompt);
        Assert.True(prompt.IndexOf("You started running.", StringComparison.Ordinal) < prompt.IndexOf("Ran again.", StringComparison.Ordinal));
        Assert.DoesNotContain("none yet", prompt);
    }

    [Theory]
    [InlineData(SummaryKind.Year, "the year 2026")]
    [InlineData(SummaryKind.Lifetime, "the person's whole lifetime")]
    public void Label_NamesEveryKind(SummaryKind kind, string expected)
    {
        Assert.Equal(expected, prompts.Label(kind.PeriodOf(new DateOnly(2026, 10, 16))));
    }

    [Fact]
    public void ChatSystem_DescribesTheFileLayoutClaudeReads()
    {
        Assert.Contains("entries/<year>/<yyyy-MM-dd>.md", prompts.ChatSystem);
        Assert.Contains("summaries/lifetime.md", prompts.ChatSystem);
    }
}
