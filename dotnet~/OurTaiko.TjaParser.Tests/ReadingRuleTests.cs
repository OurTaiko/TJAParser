using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// Fixes to how a file is read (Documentation~/Parser-TODO.md, P0).
public class ReadingRuleTests
{
    [Theory]
    [InlineData("#MEASURE 4")]
    [InlineData("#MEASURE")]
    public void AMeasureWithoutASlashFailsTheBlockAsAFormatError(string command)
    {
        var e = Assert.Throws<FormatException>(() => TjaParser.Parse($"BPM:120\nCOURSE:Oni\n#START\n{command}\n1,\n#END\n"));
        Assert.Contains("MEASURE", e.Message);
    }

    [Theory]
    [InlineData("Ura", "Edit")]
    [InlineData("4", "Edit")]
    [InlineData("Edit", "Ura")]
    [InlineData("ura", "4")]
    [InlineData("3", "oni")]
    public void ACourseIsFoundByItsDifficultyHoweverItIsSpelled(string written, string requested)
    {
        var chart = TjaParser.Parse($"BPM:120\nCOURSE:Easy\n#START\n2,\n#END\nCOURSE:{written}\nLEVEL:10\n#START\n1,\n#END\n", requested);
        Assert.Equal(10, chart.Level);
        Assert.Equal(NoteKind.Don, Assert.Single(chart.Notes).Kind);
    }

    [Fact]
    public void ADifferentDifficultyIsNotFound()
        => Assert.Throws<FormatException>(() => TjaParser.Parse("BPM:120\nCOURSE:Ura\n#START\n1,\n#END\n", "Oni"));
}
