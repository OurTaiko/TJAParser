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

    // One ChartNote has one end, so a long note the routes share cannot end on one of them
    // (TJA1017), whether a route's 8 or a route's note ends it.
    [Theory]
    [InlineData("5000,\n#BRANCHSTART p,50,80\n#N\n0080,\n#E\n8000,\n#M\n0008,\n#BRANCHEND")]
    [InlineData("5000,\n#BRANCHSTART p,50,80\n#M\n0008,\n#N\n0000,\n#BRANCHEND\n8,")]
    [InlineData("7000,\n#BRANCHSTART p,50,80\n#N\n1000,\n#BRANCHEND")]
    [InlineData("#BRANCHSTART p,50,80\n9000,\n#E\n0800,\n#BRANCHEND")]
    public void ALongNoteFromBeforeTheRoutesCannotEndOnOne(string body)
    {
        string source = "BPM:120\nCOURSE:Oni\n#START\n" + body + "\n#END";
        Assert.Contains(TjaParser.Analyze(source).Diagnostics, d => d.Kind == TjaDiagnosticKind.RollEndsInRoute);
        Assert.Throws<FormatException>(() => TjaParser.Parse(source));
    }

    [Fact]
    public void ALongNoteMayRunThroughTheWholeBranch()
    {
        // Open on every route, it ends after the branch, at the same time whatever route is played.
        var roll = Parse("5000,\n#BRANCHSTART p,50,80\n#N\n0000,\n#E\n0000,\n#M\n0000,\n#BRANCHEND\n0080,").Notes.Single();
        Assert.Equal(5, roll.EndTime);
        Assert.Equal((TjaFraction)5000000, roll.ScoreDuration);
    }

    [Fact]
    public void LongDecimalsRoundAndChangeNothingElse()
    {
        // Each value reads as a double; past forty places the scoring clock rounds it.
        string tail = new string('0', 10000) + "1";
        string Body(string delay, string bpm, string beats)
            => $"BPM:120\nCOURSE:Oni\n#START\n5\n#DELAY {delay}\n#BPMCHANGE {bpm}\n800,\n#MEASURE {beats}/4\n1,\n1,\n#END";
        var analysis = TjaParser.Analyze(Body("0.5" + tail, "240." + tail, "2." + tail));
        Assert.False(analysis.HasErrors);
        var plain = TjaParser.Parse(Body("0.5", "240", "2"));
        Assert.Equal(plain.Notes.Select(n => (n.Time, n.EndTime, n.ScoreDuration)), analysis.Chart.Notes.Select(n => (n.Time, n.EndTime, n.ScoreDuration)));
        Assert.Equal(plain.Notes.Select(n => (n.Time, n.EndTime, n.ScoreDuration)), TjaParser.Parse(Body("0.5" + tail, "240." + tail, "2." + tail)).Notes.Select(n => (n.Time, n.EndTime, n.ScoreDuration)));
    }

    // Forty places, half away from zero. A delay of 10^-40 s is 10^-34 µs.
    [Theory]
    [InlineData("0.0{39}5", 5)]
    [InlineData("0.0{40}5", 1)]
    [InlineData("-0.0{40}5", -1)]
    [InlineData("0.0{40}49", 0)]
    [InlineData("0.0{41}9", 0)]
    [InlineData("1e-2147483647", 0)]
    [InlineData("1e-99999999999", 0)]
    [InlineData("5{39}e-79", 1)]
    public void ExactNumbersRoundToFortyPlaces(string delay, int tenToMinus34)
    {
        delay = System.Text.RegularExpressions.Regex.Replace(delay, @"(\d)\{(\d+)\}", m => new string(m.Groups[1].Value[0], int.Parse(m.Groups[2].Value)));
        var source = "BPM:120\nCOURSE:Oni\n#START\n5\n#DELAY " + delay + "\n800,\n#END";
        Assert.False(TjaParser.Analyze(source).HasErrors);
        var expected = (TjaFraction)500000 + new TjaFraction(tenToMinus34, System.Numerics.BigInteger.Pow(10, 34));
        Assert.Equal(expected, Duration(TjaParser.Parse(source)));
    }

    [Fact]
    public void RoundingKeepsTheScoringClockSmall()
    {
        // Before rounding, 20000 slots of a measure with 9991 decimals took 19 s to sum exactly.
        string measure = "#MEASURE 4." + new string('0', 9990) + "1/4\n";
        var chart = Parse(measure + "5" + new string('0', 19999) + ",\n8,");
        Assert.Equal((TjaFraction)2000000, chart.Notes.Single().ScoreDuration);
        // A digit past forty places no longer counts: this scores as 512 (thirty places still do,
        // see SourceDecimalsSurviveBeyondDoubleAndDecimalPrecision).
        Assert.Equal((TjaFraction)117188, Duration(Parse("5800,", "BPM:512." + new string('0', 40) + "1")));
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
    public void RationalDefaultSignsAndRounding()
    {
        Assert.Equal(new TjaFraction(0, 5), default(TjaFraction));
        Assert.Equal(new TjaFraction(-1, 3), new TjaFraction(2, -6));
        Assert.Equal(-1, new TjaFraction(-1, 3).Floor);
        Assert.Equal(0, new TjaFraction(-1, 3).Ceiling);
        Assert.Throws<DivideByZeroException>(() => new TjaFraction(1, 0));
    }
}
