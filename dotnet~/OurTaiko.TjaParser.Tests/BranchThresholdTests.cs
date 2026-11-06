using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// ChartBranch.Thresholds: fumen's thresholds by the route being played when the condition is judged.
public class BranchThresholdTests
{
    static TaikoChart Parse(string body) => TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n" + body + "#END\n");

    [Fact]
    public void ATjaConditionGivesEveryRouteTheSamePair()
    {
        var branch = Parse("1000,\n#BRANCHSTART r, 5, 6\n#N\n1000,\n#E\n2000,\n#M\n3000,\n#BRANCHEND\n").Branches[0];
        Assert.True(branch.SameFromEveryRoute);
        foreach (var from in new[] { BranchRoute.Normal, BranchRoute.Expert, BranchRoute.Master })
        {
            Assert.Equal(5, branch.ExpertThreshold(from));
            Assert.Equal(6, branch.MasterThreshold(from));
        }
    }

    [Fact]
    public void AnEmptyConditionLeavesTheThresholdsAtZero()
    {
        var branch = Parse("1000,\n#BRANCHSTART\n#N\n1000,\n#BRANCHEND\n").Branches[0];
        Assert.Equal(BranchCondition.None, branch.Condition);
        Assert.True(branch.SameFromEveryRoute);
        Assert.Equal(0, branch.MasterThreshold(BranchRoute.Normal));
    }

    [Theory]
    [InlineData(5, BranchRoute.Normal)]
    [InlineData(85, BranchRoute.Expert)]
    [InlineData(95, BranchRoute.Master)]
    public void TheMasterThresholdIsTriedFirst(double value, BranchRoute expected)
    {
        var branch = new ChartBranch();
        branch.SetThresholds(80, 90);
        Assert.Equal(expected, branch.Choose(value, BranchRoute.Normal));
    }

    [Theory]
    [InlineData(5, BranchRoute.Master)]
    [InlineData(85, BranchRoute.Expert)]
    [InlineData(95, BranchRoute.Normal)]
    public void LessMakesThemUpperBounds(double value, BranchRoute expected)
    {
        var branch = new ChartBranch { Less = true };
        branch.SetThresholds(90, 80);
        Assert.Equal(expected, branch.Choose(value, BranchRoute.Normal));
    }

    // 聖徳たいこの「日いずるまで飛鳥」(おに), the second drumroll: from 玄人 7 hits keep 玄人 and 8
    // reach 達人; from 達人 7 hits drop to 普通 (達人 needs 8 and 玄人 is out of reach); once in
    // 普通 the player stays there. One TJA pair cannot say this.
    [Theory]
    [InlineData(BranchRoute.Normal, 20, BranchRoute.Normal)]
    [InlineData(BranchRoute.Expert, 6, BranchRoute.Normal)]
    [InlineData(BranchRoute.Expert, 7, BranchRoute.Expert)]
    [InlineData(BranchRoute.Expert, 8, BranchRoute.Master)]
    [InlineData(BranchRoute.Master, 7, BranchRoute.Normal)]
    [InlineData(BranchRoute.Master, 8, BranchRoute.Master)]
    public void EachRouteCanHaveItsOwnPair(BranchRoute from, double hits, BranchRoute expected)
    {
        var branch = new ChartBranch { Condition = BranchCondition.Drumroll };
        branch.SetThresholds(BranchRoute.Normal, double.PositiveInfinity, double.PositiveInfinity);
        branch.SetThresholds(BranchRoute.Expert, 7, 8);
        branch.SetThresholds(BranchRoute.Master, 8, 8);
        Assert.False(branch.SameFromEveryRoute);
        Assert.Equal(expected, branch.Choose(hits, from));
    }

    // Rows that differ are written after the rest of the branch line, so a TJA chart's canonical
    // text, and its hash, stay what they were.
    [Fact]
    public void RowsThatDifferAreAddedToTheCanonicalText()
    {
        var chart = Parse("1000,\n#BRANCHSTART r, 7, 8\n#N\n1000,\n#E\n2000,\n#M\n3000,\n#BRANCHEND\n");
        string before = ChartCanonical.Write(chart);
        Assert.DoesNotContain(" from ", before);

        chart.Branches[0].SetThresholds(BranchRoute.Master, 8, 8);
        string after = ChartCanonical.Write(chart);
        Assert.Contains(" from 7 8 8 8\n", after);
        Assert.Equal(before.Replace(" from 7 8 8 8", ""), after.Replace(" from 7 8 8 8", ""));
        Assert.NotEqual(ChartHash.OfCanonical(before), ChartHash.OfCanonical(after));
    }
}
