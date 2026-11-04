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

    // As OpenTaiko reads a command name (# and capital letters), a letter right after #START makes
    // another command; anything else after it is the player.
    [Theory]
    [InlineData("#STARTP1", false)]
    [InlineData("#STARTX", false)]
    [InlineData("#Startx", false)]
    [InlineData("#START", true)]
    [InlineData("#start p2", true)]
    [InlineData("#START\tP1", true)]
    [InlineData("#START1", true)]
    public void StartIsACommandOfItsOwn(string line, bool start)
        => Assert.Equal(start, TjaLines.Read(line).First().Kind == TjaLineKind.Start);

    // Only the BPM, OFFSET and LEVEL in force for the block played are read.
    [Theory]
    [InlineData("COURSE:Easy\nLEVEL:x\n#START\n2,\n#END\nCOURSE:Oni\nLEVEL:9\n")]
    [InlineData("COURSE:Easy\nBPM:x\n#START\n2,\n#END\nCOURSE:Oni\nBPM:150\nLEVEL:9\n")]
    [InlineData("OFFSET:x\nCOURSE:Oni\nOFFSET:-1\nLEVEL:9\n")]
    public void AnUnreadableValueThatIsNotInForceDoesNotFailTheBlock(string headers)
    {
        var chart = TjaParser.Parse("BPM:150\nOFFSET:-1\n" + headers + "#START\n1,\n#END\n");
        Assert.Equal(9, chart.Level);
        Assert.Equal(150, chart.Bpm);
        Assert.Equal(-1, chart.Offset);
    }

    [Theory]
    [InlineData("LEVEL:x\n")]
    [InlineData("BPM:x\n")]
    [InlineData("OFFSET:x\n")]
    [InlineData("LEVEL:\n")]
    public void AnUnreadableValueInForceStillFailsTheBlock(string header)
        => Assert.Throws<FormatException>(() => TjaParser.Parse("BPM:120\nCOURSE:Oni\n" + header + "#START\n1,\n#END\n"));

    [Fact]
    public void AnotherCoursesLevelIsNotInForce()
        => Assert.Equal(0, TjaParser.Parse("BPM:120\nCOURSE:Easy\nLEVEL:3\nCOURSE:Oni\n#START\n1,\n#END\n").Level);

    // A branch command's warning carries its own line, though commands before it were just run.
    [Fact]
    public void ABranchCommandIsWarnedAboutOnItsOwnLine()
        => Assert.Contains("#BRANCHSTART x,1,2 (line 5)",
            TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n#SCROLL 2\n#BRANCHSTART x,1,2\n#N\n1,\n#BRANCHEND\n#END\n").Warnings);
}
