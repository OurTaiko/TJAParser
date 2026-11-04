using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// #LANE_ALPHA target[%] [ms]: the lane background's opacity (an OurTaiko command).
public class LaneAlphaTests
{
    // 120 BPM: a measure is 2 s, so "1000," puts the command after it at 2 s.
    static TaikoChart Parse(string body) => TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n" + body + "#END\n");

    static ChartLaneAlpha Single(string body) => Assert.Single(Parse(body).LaneAlphas);

    [Fact]
    public void WithoutATimeItSwitchesAtOnce()
    {
        var a = Single("1000,\n#LANE_ALPHA 40\n1000,\n");
        Assert.Equal(2.0, a.Time);
        Assert.Equal(2.0, a.StartTime);
        Assert.Equal(2.0, a.EndTime);
        Assert.Equal(0.4, a.Alpha);
    }

    [Fact]
    public void APositiveTimeFadesFromTheCommand()
    {
        var a = Single("1000,\n#LANE_ALPHA 0 100\n1000,\n");
        Assert.Equal(2.0, a.StartTime);
        Assert.Equal(2.1, a.EndTime, 9);
        Assert.Equal(0.0, a.Alpha);
    }

    [Fact]
    public void ANegativeTimeFadesUpToTheCommand()
    {
        var a = Single("1000,\n#LANE_ALPHA 100 -500\n1000,\n");
        Assert.Equal(2.0, a.Time);
        Assert.Equal(1.5, a.StartTime, 9);
        Assert.Equal(2.0, a.EndTime);
    }

    [Fact]
    public void ACommandInsideAMeasureStandsBetweenItsNotes()
    {
        var a = Single("10\n#LANE_ALPHA 50\n10,\n");
        Assert.Equal(1.0, a.Time);
    }

    [Theory]
    [InlineData("#LANE_ALPHA 50%", 0.5)]
    [InlineData("#lane_alpha 25", 0.25)]
    [InlineData("#LANE_ALPHA 12.5 0", 0.125)]
    [InlineData("#LANE_ALPHA\t100\t0", 1.0)]
    [InlineData("#LANE_ALPHA 0", 0.0)]
    public void TargetsReadAsPercentages(string command, double alpha)
    {
        Assert.Equal(alpha, Single(command + "\n1000,\n").Alpha, 9);
    }

    [Fact]
    public void AFractionalTimeIsCutToWholeMilliseconds()
    {
        var a = Single("#LANE_ALPHA 50 100.9\n1000,\n");
        Assert.Equal(0.1, a.EndTime - a.StartTime, 9);
        var b = Single("#LANE_ALPHA 50 0.5\n1000,\n");
        Assert.Equal(b.StartTime, b.EndTime);
    }

    [Theory]
    [InlineData("#LANE_ALPHA")]
    [InlineData("#LANE_ALPHA 101")]
    [InlineData("#LANE_ALPHA -1")]
    [InlineData("#LANE_ALPHA abc")]
    [InlineData("#LANE_ALPHA nan")]
    [InlineData("#LANE_ALPHA 50 x")]
    [InlineData("#LANE_ALPHA 50 inf")]
    [InlineData("#LANE_ALPHA 50 100 7")]
    public void AnUnreadableCommandIsDroppedWithAWarning(string command)
    {
        var chart = Parse(command + "\n1000,\n");
        Assert.Empty(chart.LaneAlphas);
        Assert.Contains(command, chart.Warnings);
        Assert.Single(chart.Notes);
    }

    [Fact]
    public void CommandsAreInStartTimeOrder()
    {
        // The second command (at 4 s, fading from 1 s) starts before the first (at 2 s).
        var chart = Parse("1000,\n#LANE_ALPHA 10\n1000,\n#LANE_ALPHA 90 -3000\n1000,\n");
        Assert.Equal(new[] { 1.0, 2.0 }, chart.LaneAlphas.Select(a => a.StartTime));
        Assert.Equal(new[] { 0.9, 0.1 }, chart.LaneAlphas.Select(a => a.Alpha));
    }

    [Fact]
    public void CommandsOnARouteKeepTheirRoute()
    {
        var chart = Parse("1000,\n#BRANCHSTART p,50,80\n#N\n#LANE_ALPHA 10\n1000,\n#M\n#LANE_ALPHA 80\n1000,\n#BRANCHEND\n#LANE_ALPHA 100\n1000,\n");
        var routes = chart.LaneAlphas.Select(a => (a.BranchId, a.Route, a.Alpha)).ToList();
        Assert.Contains((0, BranchRoute.Normal, 0.1), routes);
        Assert.Contains((0, BranchRoute.Master, 0.8), routes);
        Assert.Contains(chart.LaneAlphas, a => a.BranchId == -1 && a.Alpha == 1.0);
        Assert.DoesNotContain(chart.Warnings, w => w.Contains("LANE_ALPHA"));
    }

    [Fact]
    public void ARouteFadeThatStartsBeforeTheDecisionIsWarned()
    {
        // The branch at 4 s is decided at the bar before it, 2 s.
        var chart = Parse("1000,\n1000,\n#BRANCHSTART p,50,80\n#M\n#LANE_ALPHA 10 -3000\n1000,\n#BRANCHEND\n");
        Assert.Single(chart.LaneAlphas);
        Assert.Contains(chart.Warnings, w => w.StartsWith("#LANE_ALPHA on a route starts before branch 0"));
    }

    [Fact]
    public void ItLeavesTheHashAlone()
    {
        Assert.Equal(ChartHash.Of(Parse("1000,\n1000,\n")), ChartHash.Of(Parse("1000,\n#LANE_ALPHA 30 -200\n1000,\n")));
    }

    // LaneAlpha.At: opaque before anything, linear while fading, the latest start in charge.
    [Fact]
    public void AtFollowsTheFades()
    {
        var chart = Parse("1000,\n#LANE_ALPHA 0 1000\n1000,\n#LANE_ALPHA 50\n1000,\n");
        var alphas = chart.LaneAlphas;
        Assert.Equal(1.0, LaneAlpha.At(alphas, 0));
        Assert.Equal(1.0, LaneAlpha.At(alphas, 2.0));
        Assert.Equal(0.5, LaneAlpha.At(alphas, 2.5), 9);
        Assert.Equal(0.0, LaneAlpha.At(alphas, 3.5));
        Assert.Equal(0.5, LaneAlpha.At(alphas, 4.0));
        Assert.Equal(0.5, LaneAlpha.At(alphas, 100));
    }

    [Fact]
    public void ANegativeFadeEndsOnTheCommand()
    {
        var alphas = Parse("1000,\n#LANE_ALPHA 0 -1000\n1000,\n").LaneAlphas;
        Assert.Equal(1.0, LaneAlpha.At(alphas, 0.9));
        Assert.Equal(0.5, LaneAlpha.At(alphas, 1.5), 9);
        Assert.Equal(0.0, LaneAlpha.At(alphas, 2.0));
    }

    [Fact]
    public void ALaterStartTakesOverFromWhereTheFadeIs()
    {
        // 0 over 2..4 s; at 3 s (half way, 0.5) a fade to 1 over a second takes over.
        var alphas = Parse("1000,\n#LANE_ALPHA 0 2000\n10\n#LANE_ALPHA 100 1000\n00,\n1000,\n").LaneAlphas;
        Assert.Equal(0.5, LaneAlpha.At(alphas, 3.0), 9);
        Assert.Equal(0.75, LaneAlpha.At(alphas, 3.5), 9);
        Assert.Equal(1.0, LaneAlpha.At(alphas, 4.0), 9);
        // The first fade, cut off, no longer counts after its own end.
        Assert.Equal(1.0, LaneAlpha.At(alphas, 4.5), 9);
    }

    [Fact]
    public void AtReadsOnlyTheRoutesPlayed()
    {
        var alphas = Parse("1000,\n#BRANCHSTART p,50,80\n#N\n#LANE_ALPHA 10\n1000,\n#M\n#LANE_ALPHA 80\n1000,\n#BRANCHEND\n").LaneAlphas;
        Assert.Equal(0.8, LaneAlpha.At(alphas, 3, a => a.BranchId < 0 || a.Route == BranchRoute.Master));
        Assert.Equal(0.1, LaneAlpha.At(alphas, 3, a => a.BranchId < 0 || a.Route == BranchRoute.Normal));
        Assert.Equal(1.0, LaneAlpha.At(alphas, 3, a => a.BranchId < 0));
    }
}
