using Sufler.Core.Script;

namespace Sufler.Tests.Script;

public sealed class ScriptTextTests
{
    [Fact]
    public void Normalize_Null_ReturnsEmpty()
        => Assert.Equal(string.Empty, ScriptText.Normalize(null));

    [Fact]
    public void Normalize_StripsLeadingBom()
        => Assert.Equal("hello", ScriptText.Normalize("\uFEFFhello"));

    [Fact]
    public void Normalize_ConvertsCrLfAndLoneCrToLf()
    {
        Assert.Equal("a\nb\nc", ScriptText.Normalize("a\r\nb\r\nc"));
        Assert.Equal("a\nb", ScriptText.Normalize("a\rb"));
    }

    [Fact]
    public void Normalize_StripsTrailingWhitespacePerLine()
        => Assert.Equal("a\nb", ScriptText.Normalize("a   \nb\t\t  \n"));

    [Fact]
    public void Normalize_DropsLeadingAndTrailingBlankLines()
        => Assert.Equal("a\nb", ScriptText.Normalize("\n\n\na\nb\n\n\n"));

    [Fact]
    public void Normalize_CollapsesThreeOrMoreNewlinesToTwo()
    {
        Assert.Equal("a\n\nb", ScriptText.Normalize("a\n\n\n\n\nb"));
        Assert.Equal("a\n\nb", ScriptText.Normalize("a\n\nb"));
    }

    [Fact]
    public void Normalize_WhitespaceOnlyBecomesEmpty()
        => Assert.Equal(string.Empty, ScriptText.Normalize("   \n\t \n"));

    [Theory]
    [InlineData("\uFEFFhello\r\nworld  \n\n\n\nagain")]
    [InlineData("\n\nplain\ntext\n\n")]
    [InlineData("")]
    [InlineData("single")]
    public void Normalize_IsIdempotent(string raw)
    {
        var once = ScriptText.Normalize(raw);

        Assert.Equal(once, ScriptText.Normalize(once));
    }

    [Fact]
    public void SplitLogicalLines_EmptyInputReturnsEmptyList()
    {
        var lines = ScriptText.SplitLogicalLines(string.Empty);

        Assert.NotNull(lines);
        Assert.Empty(lines);
    }

    [Fact]
    public void SplitLogicalLines_SingleLineReturnsOneEntry()
    {
        var lines = ScriptText.SplitLogicalLines("only line");

        Assert.Equal(new[] { "only line" }, lines);
    }

    [Fact]
    public void SplitLogicalLines_PreservesInteriorBlankLines()
    {
        var lines = ScriptText.SplitLogicalLines("first\n\nthird");

        Assert.Equal(3, lines.Count);
        Assert.Equal("first", lines[0]);
        Assert.Equal(string.Empty, lines[1]);
        Assert.Equal("third", lines[2]);
    }

    [Fact]
    public void SplitLogicalLines_NormalizedTextHasNoCarriageReturns()
    {
        var normalized = ScriptText.Normalize("a\r\nb\rc");
        var lines = ScriptText.SplitLogicalLines(normalized);

        Assert.Equal(new[] { "a", "b", "c" }, lines);
    }
}