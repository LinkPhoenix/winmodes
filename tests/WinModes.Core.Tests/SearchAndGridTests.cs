namespace WinModes.Core.Tests;

public sealed class SearchAndGridTests
{
    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("xbox", true)]
    [InlineData("XBOX game", true)]
    [InlineData("game xbox", true)]
    [InlineData("xbox calendar", false)]
    public void EveryWordMustBeFoundInSomeField(string query, bool expected) =>
        Assert.Equal(expected, SearchMatcher.Matches(query, "Xbox app", "Game bar and captures", null));

    [Fact]
    public void ANullQueryMatchesEverything() => Assert.True(SearchMatcher.Matches(null, "anything"));

    [Fact]
    public void TermsAreSplitOnAnyWhitespace() => Assert.Equal(["a", "b", "c"], SearchMatcher.Terms("  a\tb   c "));

    [Fact]
    public void PreparsedTermsCanMatchDifferentFields() =>
        Assert.True(SearchMatcher.MatchesTerms(SearchMatcher.Terms("xbox game"), "Xbox app", "Game bar and captures"));

    [Fact]
    public void PreparsedTermsRequireEveryWord() =>
        Assert.False(SearchMatcher.MatchesTerms(SearchMatcher.Terms("xbox calendar"), "Xbox app", "Game bar and captures"));

    [Theory]
    [InlineData(300, 300, 12, 1)]
    [InlineData(611, 300, 12, 1)]
    [InlineData(612, 300, 12, 2)]
    [InlineData(1100, 300, 12, 3)]
    [InlineData(2000, 300, 12, 3)]
    [InlineData(100, 300, 12, 1)]
    public void ColumnsComeFromTheWidth(double width, double min, double gap, int expected) =>
        Assert.Equal(expected, CardGridMath.ColumnCount(width, min, gap, maxColumns: 3));

    [Fact]
    public void ABrokenWidthStillGivesOneColumn()
    {
        Assert.Equal(1, CardGridMath.ColumnCount(double.PositiveInfinity, 300, 12));
        Assert.Equal(1, CardGridMath.ColumnCount(double.NaN, 300, 12));
    }

    [Fact]
    public void TheCardsShareTheWidthAfterTheGaps() => Assert.Equal(344, CardGridMath.ItemWidth(1048, 3, 8));
}
