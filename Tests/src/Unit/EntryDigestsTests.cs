namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class EntryDigestsTests
{
    [Theory]
    [InlineData("Ran 5k", "Ran 5k")]
    [InlineData("  Ran   5k \n\n this\tmorning  ", "Ran 5k this morning")]
    [InlineData("", "")]
    [InlineData("   \n ", "")]
    public void ToExcerpt_PutsTheTextOnOneLineWithSingleSpaces(string text, string expected)
    {
        Assert.Equal(expected, text.ToExcerpt());
    }

    [Fact]
    public void ToExcerpt_CutsLongTextAtOneHundredSixtyCharactersAndMarksTheCut()
    {
        string excerpt = new string('a', 500).ToExcerpt();

        Assert.Equal(163, excerpt.Length);
        Assert.EndsWith("...", excerpt, StringComparison.Ordinal);
    }

    [Fact]
    public void ToExcerpt_TextOfExactlyTheLimitIsNotCut()
    {
        Assert.Equal(new string('a', 160), new string('a', 160).ToExcerpt());
    }
}
