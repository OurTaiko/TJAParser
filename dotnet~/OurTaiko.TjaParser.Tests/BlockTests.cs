using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

public class BlockTests
{
    [Fact]
    public void BlocksAreKeyedByCourseAndSideInSongInfoOrder()
    {
        var blocks = ChartHash.OfFile(Tja.Sample);
        Assert.Equal(new[] { "Oni/single", "Oni/p1", "Oni/p2", "Edit/single" }, blocks.Select(b => b.Course + "/" + b.Side));
        Assert.Equal(new[] { 8, 0, 0, 10 }, blocks.Select(b => b.Level));
        Assert.Equal(Difficulty.Ura, blocks[3].Difficulty);
        Assert.All(blocks, b => Assert.False(b.IsBranching));
        Assert.True(ChartHash.OfFile(Tja.Branching)[0].IsBranching);
    }

    [Fact]
    public void ASecondBlockOfTheSameCourseAndSideIsNotListed()
    {
        string text = Tja.Sample + "COURSE:Oni\n#START\n3333,\n#END\n";
        var before = Tja.Hashes(Tja.Sample);
        var after = Tja.Hashes(text);
        Assert.Equal(before, after);
    }

    [Fact]
    public void DifferentBlocksHaveDifferentHashes()
    {
        var hashes = Tja.Hashes(Tja.Sample);
        Assert.Equal(4, hashes.Values.Distinct().Count());
    }

    [Fact]
    public void TheSameNotesOnAnotherSideOrCourseHashTheSame()
    {
        // The hash is the block's content; which course or side it sits on is the key, not the content.
        string text = "BPM:120\nCOURSE:Oni\n#START\n1122,\n#END\nCOURSE:Hard\n#START P2\n1122,\n#END\n";
        var hashes = Tja.Hashes(text);
        Assert.Equal(hashes["Oni/single"], hashes["Hard/p2"]);
    }

    [Fact]
    public void AnUnreadableBlockReportsItsErrorAndLeavesTheOthers()
    {
        // A duplicate route marker is still an error (OpenTaiko tolerances are in ToleranceTests).
        string text = Tja.Sample.Replace("1111,", "#BRANCHSTART p,50,80\n#N\n1111,\n#N\n1111,\n#BRANCHEND\n");
        var blocks = Tja.Blocks(text);
        Assert.False(blocks["Edit/single"].Ok);
        Assert.Null(blocks["Edit/single"].Hash);
        Assert.Contains("Duplicate branch route", blocks["Edit/single"].Error);
        Assert.True(blocks["Oni/single"].Ok);
        Assert.Equal(Tja.Hashes(Tja.Sample)["Oni/single"], blocks["Oni/single"].Hash);
    }

    [Fact]
    public void AnUnknownCourseIsNotABlock()
    {
        string text = Tja.Sample + "COURSE:Tower\n#START\n1,\n#END\n";
        Assert.Equal(4, ChartHash.OfFile(text).Count);
    }

    [Fact]
    public void AnEmptyFileThrows()
    {
        Assert.Throws<FormatException>(() => ChartHash.OfFile("  \n"));
        Assert.Empty(ChartHash.OfFile("TITLE:Nothing\nBPM:120\n"));
    }
}
