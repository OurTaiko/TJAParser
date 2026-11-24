using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

public class ScoreDurationTests
{
    static TaikoChart Parse(string body, string headers = "", string side = "single")
        => TjaParser.Parse("BPM:120\nCOURSE:Oni\n" + headers + "\n#START" + (side == "single" ? "" : " " + side.ToUpperInvariant()) + "\n" + body + "\n#END", "Oni", side);
    static TjaFraction Duration(TaikoChart chart) => chart.Notes.Where(n => n.IsDrumroll).Aggregate(default(TjaFraction), (sum, n) => sum + n.ScoreDuration);

    [Fact]
    public void SixBeatsAt180AreNotTwoSeconds()
    {
        var chart = Parse("5000,\n0080,", "BPM:180");
        Assert.Equal((TjaFraction)1999998, Duration(chart));
        Assert.Equal(2, chart.Notes[0].EndTime);
    }

    [Theory]
    [InlineData(3, 2, 1375000, 1)]
    [InlineData(96, 1, 125000, 3)]
    public void OffGridTailAddsAtMostOneSlot(int slots, int tail, int numerator, int denominator)
    {
        var chart = Parse("5" + new string('0', tail - 1) + "8" + new string('0', slots - tail - 1) + ",");
        Assert.Equal(new TjaFraction(numerator, denominator), Duration(chart));
    }

    [Fact]
    public void DurationsRemainFractionalUntilTheConsumerSumsThem()
    {
        string roll = "58" + new string('0', 94) + ",\n";
        var chart = Parse(roll + roll);
        Assert.All(chart.Notes, n => Assert.Equal(new TjaFraction(125000, 3), n.ScoreDuration));
        Assert.Equal(new TjaFraction(250000, 3), Duration(chart));
        Assert.Equal(1, (Duration(chart) * 15 / 1000000).Floor);
    }

    [Theory]
    [InlineData("#DELAY 10\n50\n#BPMCHANGE 240\n#DELAY .25\n08,\n#DELAY 10", 1500000, 1)]
    [InlineData("#MEASURE 1/3\n5008,", 500000, 1)]
    [InlineData("5000,\n,\n0080,", 5000000, 1)]
    [InlineData("5008", 1500000, 1)]
    [InlineData("50/* comment */08 // no comma", 1500000, 1)]
    [InlineData("5\n#MEASURE 3/4\n008,", 1250000, 1)]
    [InlineData("50\n#DELAY -.25\n08,", 1250000, 1)]
    [InlineData("50\n#DELAY -3\n08,", -1500000, 1)]
    [InlineData("5000,", 2000000, 1)]
    [InlineData("5001,", 1500000, 1)]
    public void TimingUsesTheExistingParserTraversal(string body, int numerator, int denominator)
        => Assert.Equal(new TjaFraction(numerator, denominator), Duration(Parse(body)));

    [Fact]
    public void SourceDecimalsSurviveBeyondDoubleAndDecimalPrecision()
    {
        // Just above 512: the half-microsecond rounds down, although a double reads exactly 512.
        var chart = Parse("5800,", "BPM:512.000000000000000000000000000001");
        Assert.Equal((TjaFraction)117187, Duration(chart));
        Assert.Equal((TjaFraction)117188, Duration(Parse("5800,", "BPM:512")));
        Assert.Equal((TjaFraction)117187, Duration(Parse("#BPMCHANGE 512.000000000000000000000000000001\n5800,")));
        var exact = new TjaFraction(System.Numerics.BigInteger.Parse("1000000000000000000000000000001"), System.Numerics.BigInteger.Pow(10, 24));
        Assert.Equal((TjaFraction)500000 + exact, Duration(Parse("5\n#DELAY 1.000000000000000000000000000001\n800,")));
    }

    [Fact]
    public void MeasureUsesSourceDecimalRatherThanThePlaybackFloat()
    {
        var chart = Parse("#MEASURE 1.000000000000000000000000000001/1\n5800,");
        var beat = new TjaFraction(System.Numerics.BigInteger.Parse("1000000000000000000000000000001"), System.Numerics.BigInteger.Pow(10, 30));
        Assert.Equal((beat + new TjaFraction(1, 12)) * 500000, Duration(chart));
    }

    [Theory]
    [InlineData("p1")]
    [InlineData("p2")]
    public void PlayerBlocksHaveTheSameTiming(string side)
        => Assert.Equal((TjaFraction)1500000, Duration(Parse("5008,", side: side)));

    [Fact]
    public void BranchesRestoreTheirOwnScoringClock()
    {
        var chart = Parse("5008,\n#BRANCHSTART p,50,80\n#N\n#BPMCHANGE 240\n5008,\n#E\n5008,\n#M\n#MEASURE 3/4\n5008,\n#BRANCHEND\n5008,");
        Assert.Equal(new TjaFraction[] { 1500000, 750000, 1500000, 1125000, 1125000 }, chart.Notes.Select(n => n.ScoreDuration));
    }

    [Fact]
    public void SharedRollKeepsTheFirstRoutesTailLikePlayback()
    {
        var chart = Parse("5000,\n#BRANCHSTART p,50,80\n#N\n0080,\n#E\n8000,\n#M\n0008,\n#BRANCHEND");
        Assert.Equal((TjaFraction)3000000, Duration(chart));
        Assert.Equal(3, chart.Notes.Single().EndTime);
    }

    [Fact]
    public void ScoringFieldsDoNotChangeCanonicalContent()
    {
        var chart = Parse("5008,");
        string before = ChartCanonical.Write(chart);
        chart.Notes[0].ScoreDuration = new TjaFraction(1, 3);
        Assert.Equal(before, ChartCanonical.Write(chart));
    }

    [Theory]
    [InlineData("120,0", 500000)]
    [InlineData("1.2e2,", 500000)]
    [InlineData("+120.0", 500000)]
    public void ExistingNumberSpellingsWork(string bpm, int micros)
        => Assert.Equal((TjaFraction)micros, Duration(Parse("5800,", "BPM:" + bpm)));

    [Fact]
    public void ExactNumberLimitsUseNormalDiagnostics()
    {
        const string source = "BPM:120\n#START\n5\n#DELAY 1e-2147483647\n800,\n#END";
        Assert.Throws<FormatException>(() => TjaParser.Parse(source));
        Assert.True(TjaParser.Analyze(source).HasErrors);
    }

    [Fact]
    public void RationalDefaultSignsAndRounding()
    {
        Assert.Equal(new TjaFraction(0, 5), default(TjaFraction));
        Assert.Equal(new TjaFraction(-1, 3), new TjaFraction(2, -6));
        Assert.Equal(-1, new TjaFraction(-1, 3).Floor);
        Assert.Equal(0, new TjaFraction(-1, 3).Ceiling);
        Assert.Throws<DivideByZeroException>(() => new TjaFraction(1, 0));
    }
}
