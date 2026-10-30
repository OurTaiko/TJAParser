using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// `7[10]` / `9[10]` (not in OpenTaiko): a balloon's count written on the note itself.
public class InlineBalloonTests
{
    static TaikoChart Parse(string headers, string body) => TjaParser.Parse("BPM:120\nCOURSE:Oni\n" + headers + "#START\n" + body + "#END\n");

    static int[] Hits(TaikoChart chart) => chart.Notes.Where(n => n.IsBalloon).Select(n => n.BalloonHits).ToArray();

    [Fact]
    public void AnInlineCountTakesNoSlot()
    {
        var inline = Parse("", "7[10]008,\n9[ 2 0 ]008,\n");
        Assert.Equal(new[] { 10, 20 }, Hits(inline));
        Assert.Equal(new[] { 1.5, 3.5 }, inline.Notes.Select(n => n.EndTime));
        // The same chart written with BALLOON hashes the same.
        Assert.Equal(ChartHash.Of(Parse("BALLOON:10,20\n", "7008,\n9008,\n")), ChartHash.Of(inline));
    }

    [Fact]
    public void AnInlineCountStillTakesItsPlaceInTheList()
    {
        Assert.Equal(new[] { 1, 30, 3 }, Hits(Parse("BALLOON:1,2,3\n", "7008,\n7[30]008,\n7008,\n")));
    }

    [Fact]
    public void InARouteItIsThatRoutesCountAndInCommonEveryRoutes()
    {
        var chart = Parse("BALLOONNOR:1,2\nBALLOONEXP:3,4\nBALLOONMAS:5,6\n",
            "7[9]008,\n#BRANCHSTART p,50,80\n#N\n7008,\n#E\n7[40]008,\n#M\n7008,\n#BRANCHEND\n");
        Assert.Equal(9, chart.Notes.Single(n => n.IsBalloon && n.BranchId < 0).BalloonHits);
        var routed = chart.Notes.Where(n => n.IsBalloon && n.BranchId >= 0).ToDictionary(n => n.Route, n => n.BalloonHits);
        Assert.Equal(2, routed[BranchRoute.Normal]);
        Assert.Equal(40, routed[BranchRoute.Expert]);
        Assert.Equal(6, routed[BranchRoute.Master]);
    }

    [Theory]
    [InlineData("7[0]008,\n")]
    [InlineData("7[-1]008,\n")]
    [InlineData("7[1.5]008,\n")]
    [InlineData("7[abc]008,\n")]
    [InlineData("7[]008,\n")]
    [InlineData("7[99999999999]008,\n")]
    [InlineData("7[10008,\n")]
    [InlineData("7[1\n0]008,\n")]
    [InlineData("1[10]000,\n")]
    [InlineData("5[10]008,\n")]
    [InlineData("7008[10],\n")]
    [InlineData("7[1][2]008,\n")]
    [InlineData("7008,\n[10]1,\n")]
    [InlineData("7008,[10]\n")]
    public void AMalformedCountThrows(string body)
    {
        Assert.Throws<FormatException>(() => Parse("", body));
    }
}
