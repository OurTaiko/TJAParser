using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

public class ShinuchiScoringTests
{
    static TaikoChart Parse(string body, string course = "Oni", string metadata = "")
        => TjaParser.Parse("BPM:120\nCOURSE:" + course + "\n" + metadata + "\n#START\n" + body + "\n#END", course);
    static ShinuchiScoring Score(string body, string course = "Oni", string metadata = "")
        => ShinuchiScoring.Of(Parse(body, course, metadata));

    [Theory]
    [InlineData("1110,", "", 333340, 1000020)]
    [InlineData("1111,", "", 250000, 1000000)]
    // Oni, two notes and a three-second roll: k = 45, H = 51, A = 5100.
    [InlineData("1000,\n5000,\n0080,\n1000,", "", 497450, 1000000)]
    [InlineData("1234,\n5008,\n7008,\n9008,", "BALLOON:150,2", 245580, 1000020)]
    [InlineData("0000,", "", 1000000, 1000000)]
    public void GoodPointsAndKiwamiFollowTheFormula(string body, string metadata, int good, int kiwami)
    {
        var score = Score(body, metadata: metadata);
        Assert.Equal(good, score.GoodPoints);
        Assert.Equal(kiwami, score.KiwamiThreshold);
    }

    [Fact]
    public void OkPointsAreHalfTheGoodPoints()
    {
        Assert.Equal(166670, Score("1110,").OkPoints);
        Assert.Equal(248720, Score("1000,\n5000,\n0080,\n1000,").OkPoints);
        Assert.Equal(500000, Score("0000,").OkPoints);
    }

    [Theory]
    [InlineData("Easy", 998900)]
    [InlineData("Normal", 998700)]
    [InlineData("Hard", 998300)]
    [InlineData("Oni", 997500)]
    [InlineData("Edit", 997500)]
    [InlineData("Ura", 997500)]
    [InlineData("4", 997500)]
    public void RollBudgetUsesTheCourseRate(string course, int good)
    {
        var score = Score("1000,\n5008,", course);
        Assert.Equal(good, score.GoodPoints);
        Assert.Equal(1000000, score.KiwamiThreshold);
    }

    [Fact]
    public void AllRollsAreSummedBeforeTheSingleFloorAndCeiling()
    {
        string roll = "58" + new string('0', 94) + ",\n";
        Assert.Equal(999800, Score("1000,\n" + roll + roll).GoodPoints);
        Assert.Equal(996700, Score("1000,\n5000,\n0080,", metadata: "BPM:180").GoodPoints);
    }

    [Theory]
    [InlineData("1110,\n7008,", 328340, 1000020)]
    [InlineData("1110,\n9008,", 328340, 1000020)]
    [InlineData("111G,\n5008,", 249380, 1000020)]
    [InlineData("111F,\n5008,", 332500, 1000000)]
    [InlineData("111C,\n5008,", 332500, 1000000)]
    [InlineData("1110,\n5000,", 332200, 1000000)]
    [InlineData("1110,\n5\n#MEASURE 3/4\n008,", 332640, 1000020)]
    public void SpecialNotesAndToleratedSyntaxUseOneFormula(string body, int good, int kiwami)
    {
        var score = Score(body, metadata: "BALLOON:150");
        Assert.Equal(good, score.GoodPoints);
        Assert.Equal(kiwami, score.KiwamiThreshold);
    }

    [Fact]
    public void KadonIsJudgedAndAdLibAndMinesAreNot()
    {
        Assert.Equal(4, Score("111G,").JudgeableNotes);
        Assert.Equal(3, Score("111F,").JudgeableNotes);
        Assert.Equal(3, Score("111C,").JudgeableNotes);
        Assert.Equal(Score("1000,").GoodPoints, Score("1F00,").GoodPoints);
        Assert.Equal(Score("1000,").KiwamiThreshold, Score("1C00,").KiwamiThreshold);
    }

    [Theory]
    [InlineData("p1")]
    [InlineData("p2")]
    public void PlayerBlocksUseTheSameFormula(string side)
    {
        const string text = "BPM:120\nCOURSE:Oni\n#START P1\n1110,\n5008,\n#END\n#START P2\n1110,\n5008,\n#END";
        var score = ShinuchiScoring.Of(TjaParser.Parse(text, "Oni", side));
        Assert.Equal(332500, score.GoodPoints);
        Assert.Equal(1000000, score.KiwamiThreshold);
    }

    [Fact]
    public void CommonAndMasterRoutesSupplyTheBudget()
    {
        var score = Score("1000,\n5008,\n#BRANCHSTART p,50,80\n#N\n1111,\n#E\n7008,\n#M\n1100,\n5008,\n9008,\n#BRANCHEND", metadata: "BALLOONMAS:150");
        Assert.Equal(3, score.JudgeableNotes);
        Assert.Equal(new[] { 5, 1, 3 }, score.RouteNotes);
        Assert.Equal(150, score.BalloonHits);
        Assert.Equal((TjaFraction)3000000, score.RollMicroseconds);
        Assert.Equal(326640, score.GoodPoints);
        Assert.Equal(1000020, score.KiwamiThreshold);
    }

    [Fact]
    public void MissingMasterUsesTheRouteDrawnInItsPlace()
    {
        var score = Score("1000,\n#BRANCHSTART p,50,80\n#N\n1000,\n#E\n1100,\n5008,\n#BRANCHEND");
        Assert.Equal(3, score.JudgeableNotes);
        Assert.Equal(new[] { 2, 3, 3 }, score.RouteNotes);
        Assert.Equal(332500, score.GoodPoints);
    }

    [Fact]
    public void SharedRollUsesTheMasterRoutesDuration()
    {
        // Normal ends the roll first at 3 s; Master at 3.5 s: k = floor(15 × 3.5) = 52, H = 59.
        var score = Score("1000,\n5000,\n#BRANCHSTART p,50,80\n#N\n0080,\n#E\n0080,\n#M\n0008,\n#BRANCHEND");
        Assert.Equal((TjaFraction)3500000, score.RollMicroseconds);
        Assert.Equal(994100, score.GoodPoints);
    }

    [Theory]
    [InlineData(9999, 50, 1000000)]
    [InlineData(10000, 10, 1000020)]
    [InlineData(10001, 10, 1000120)]
    public void ExhaustedBudgetStillAwardsAtLeastTenPerGood(int hits, int good, int kiwami)
    {
        var score = Score("1100,\n7008,", metadata: "BALLOON:" + hits);
        Assert.Equal(good, score.GoodPoints);
        Assert.Equal(kiwami, score.KiwamiThreshold);
    }

    [Theory]
    [InlineData("0000,", 1000000)]
    [InlineData("7008,", 1000100)]
    public void NoJudgedNotesKeepsMillionPointGood(string body, int kiwami)
    {
        var score = Score(body, metadata: "BALLOON:10001");
        Assert.Equal(1000000, score.GoodPoints);
        Assert.Equal(kiwami, score.KiwamiThreshold);
    }

    [Fact]
    public void HugeBalloonBudgetsDoNotOverflowBeforeThresholdSaturation()
    {
        var score = Score("1000,\n7008,\n9008,", metadata: "BALLOON:2147483647,2147483647");
        Assert.Equal(4294967294L, score.BalloonHits);
        Assert.Equal(10, score.GoodPoints);
        Assert.Equal(int.MaxValue, score.KiwamiThreshold);
    }

    [Fact]
    public void NegativeRequirementsAndReversedRollsCannotSubtractFromBudget()
    {
        var score = Score("1000,\n5\n#DELAY -3\n008,\n7008,", metadata: "BALLOON:-5");
        Assert.Equal(0, score.BalloonHits);
        Assert.Equal(default, score.RollMicroseconds);
        Assert.Equal(1000000, score.GoodPoints);
    }

    [Fact]
    public void ConstructedChartsUseTheirExplicitDurations()
    {
        var chart = new TaikoChart();
        chart.Notes.Add(new ChartNote { Kind = NoteKind.Don });
        chart.Notes.Add(new ChartNote { Kind = NoteKind.Roll, ScoreDuration = 1500000 });
        Assert.Equal(997500, ShinuchiScoring.Of(chart).GoodPoints);
    }

    [Fact]
    public void OnlyTheRequestedCourseContributes()
    {
        const string text = "BPM:120\nCOURSE:Easy\n#START\n1110,\n#END\nCOURSE:Oni\n#START\n1110,\n5008,\n#END";
        Assert.Equal(333340, ShinuchiScoring.Of(TjaParser.Parse(text, "Easy")).GoodPoints);
        Assert.Equal(332500, ShinuchiScoring.Of(TjaParser.Parse(text, "Oni")).GoodPoints);
    }

    [Fact]
    public void ScoringIsNotPartOfTheContentHash()
    {
        var chart = Parse("1000,\n5008,");
        string before = ChartCanonical.Write(chart);
        _ = ShinuchiScoring.Of(chart);
        Assert.Equal(before, ChartCanonical.Write(chart));
    }

    [Fact]
    public void UnsupportedCourseIsRefused()
        => Assert.Throws<ArgumentException>(() => ShinuchiScoring.Of(new TaikoChart { Course = "Tower" }));
}
