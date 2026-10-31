using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// OpenTaiko's tolerance for rolls and branch structure (CTja.cs, TaikoJiro compatibility): each
// case parses, is warned about, and plays as OpenTaiko plays it.
public class ToleranceTests
{
    static TaikoChart Parse(string body) => TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n" + body + "#END\n");

    [Fact]
    public void AStray8IsABlank()
    {
        var chart = Parse("1080,\n");
        Assert.Equal(new[] { NoteKind.Don }, chart.Notes.Select(n => n.Kind));
        Assert.Contains(chart.Warnings, w => w.StartsWith("A stray 8"));
        // The blank still takes its slot: the don is the first of four sixteenths.
        Assert.Equal(0, chart.Notes[0].Time);
        Assert.Equal(2, chart.Duration);
    }

    [Fact]
    public void ANonRollEndsAnUnendedRollAndIsKept()
    {
        var chart = Parse("5001,\n");
        Assert.Equal(new[] { NoteKind.Roll, NoteKind.Don }, chart.Notes.Select(n => n.Kind));
        Assert.Equal(1.5, chart.Notes[0].EndTime);
        Assert.Equal(1.5, chart.Notes[1].Time);
        Assert.Contains(chart.Warnings, w => w.StartsWith("An unended roll is ended by a note of type 1"));
        Assert.Single(chart.Warnings);
    }

    [Fact]
    public void ARollHeadInsideARollIsABlank()
    {
        var chart = Parse("5060,\n8000,\n");
        Assert.Equal(new[] { NoteKind.Roll }, chart.Notes.Select(n => n.Kind));
        Assert.Equal(2, chart.Notes[0].EndTime);
        Assert.Contains(chart.Warnings, w => w.StartsWith("A roll head inside a roll"));
    }

    [Fact]
    public void EndEndsAnUnendedRoll()
    {
        var chart = Parse("5000,\n0000,\n");
        Assert.Equal(4, chart.Notes[0].EndTime);
        Assert.Contains("An unended roll is ended by #END", chart.Warnings);
        Assert.Equal(new[] { false, true }, chart.NoteLists[0].Where(e => !e.IsBar).Select(e => e.IsTail));
    }

    [Fact]
    public void ARollOnARouteStaysOpenAcrossTheOtherRoutes()
    {
        // The normal route's roll crosses #E, #M and the next branch, and ends at the 8 of the next #N.
        var chart = Parse("1111,\n#BRANCHSTART p,50,80\n#N\n5000,\n#E\n1111,\n#M\n2222,\n#BRANCHEND\n#BRANCHSTART p,50,80\n#N\n0080,\n#E\n1111,\n#M\n2222,\n#BRANCHEND\n");
        var roll = Assert.Single(chart.Notes, n => n.Kind == NoteKind.Roll);
        Assert.Equal(2, roll.Time);
        Assert.Equal(5, roll.EndTime);
        Assert.Equal(BranchRoute.Normal, roll.Route);
        Assert.Equal(0, roll.BranchId);
        // Head entry in the first branch's N list, tail entry in the second's.
        Assert.Contains(chart.NoteLists[1], e => e.Note == roll && !e.IsTail);
        Assert.Contains(chart.NoteLists[4], e => e.Note == roll && e.IsTail);
        Assert.Empty(chart.Warnings);
    }

    [Fact]
    public void ACommonRollIsEndedByEachRoute()
    {
        var chart = Parse("5000,\n#BRANCHSTART p,50,80\n#N\n8000,\n#E\n8000,\n#M\n0800,\n#BRANCHEND\n1111,\n");
        var roll = Assert.Single(chart.Notes, n => n.Kind == NoteKind.Roll);
        Assert.Equal(2, roll.EndTime);
        Assert.Contains(chart.Warnings, w => w.StartsWith("A roll shared by the routes ends at different times"));
        // Every route list carries its own tail entry.
        for (int list = 1; list <= 3; list++) Assert.Contains(chart.NoteLists[list], e => e.Note == roll && e.IsTail);
    }

    [Fact]
    public void ABranchWithoutNHasAnEmptyNormalRoute()
    {
        var chart = Parse("1111,\n#BRANCHSTART p,50,80\n#E\n1010,\n#M\n1111,\n#BRANCHEND\n2222,\n");
        var branch = Assert.Single(chart.Branches);
        Assert.Equal(BranchRoute.Normal, branch.Routes[0]);
        Assert.Empty(chart.NoteLists[1]);
        Assert.Equal(2, chart.NoteLists[2].Count(e => !e.IsBar));
        Assert.Contains(chart.Warnings, w => w.Contains("has no #N"));
    }

    [Fact]
    public void NotesBeforeTheFirstRouteMarkerAreCommonAndOverlapTheRoutes()
    {
        var chart = Parse("#BRANCHSTART p,50,80\n1111,\n#N\n2222,\n#E\n2222,\n#M\n2222,\n#BRANCHEND\n");
        var common = chart.Notes.Where(n => n.BranchId < 0).ToList();
        Assert.Equal(4, common.Count);
        Assert.All(common, n => Assert.Equal(NoteKind.Don, n.Kind));
        // The routes start over the common measure, as OpenTaiko rewinds to the branch start.
        Assert.Equal(0, chart.Notes.Where(n => n.BranchId == 0).Min(n => n.Time));
        Assert.Contains(chart.Warnings, w => w.StartsWith("Notes between #BRANCHSTART"));
    }

    [Fact]
    public void StrictErrorsStillThrow()
    {
        Assert.Throws<FormatException>(() => Parse("#BRANCHSTART p,50,80\n#N\n1,\n#N\n1,\n#BRANCHEND\n"));
        Assert.Throws<FormatException>(() => Parse("#N\n1,\n"));
        Assert.Throws<FormatException>(() => Parse("#BRANCHEND\n"));
    }

    [Theory]
    [InlineData("#DELAY abc")]
    [InlineData("#DELAY")]
    [InlineData("#DELAY Infinity")]
    [InlineData("#DELAY NaN")]
    public void AnUnreadableDelayIsDroppedWithAWarning(string delay)
    {
        var chart = Parse("11\n" + delay + "\n11,\n");
        Assert.Equal(new[] { 0, 0.5, 1, 1.5 }, chart.Notes.Select(n => n.Time));
        Assert.Contains(delay, chart.Warnings);
        Assert.Equal(ChartHash.Of(Parse("1111,\n")), ChartHash.Of(chart));
    }
}
