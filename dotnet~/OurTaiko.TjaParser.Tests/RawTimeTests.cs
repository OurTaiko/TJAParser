using System.Linq;
using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

public class RawTimeTests
{
    [Fact]
    public void RawMsIsTheUnroundedTimeFromStartWithDelays()
    {
        // 170 BPM sixteenths are 88.2352941… ms: Time is on the grid of a thousandth of a
        // millisecond (after OFFSET), RawMs is not; #DELAY moves both; OFFSET moves only Time.
        var chart = TjaParser.Parse("BPM:170\nOFFSET:-1\nCOURSE:Oni\n#START\n1100000000000000,\n#DELAY 0.5\n1,\n#END\n");
        double sixteenth = 15000.0 / 170;
        Assert.Equal(new[] { 0, sixteenth, 4 * 4 * sixteenth + 500 }, chart.Notes.Select(n => n.RawMs));
        Assert.Equal(new[] { 1.0, 1.088235, 2.911765 }, chart.Notes.Select(n => n.Time));
    }

    [Fact]
    public void TheSameRhythmOnAnotherGridLandsOnTheSameTimes()
    {
        // 150 BPM sixteenths are 100 ms apart. Written on 48 slots the running sum reaches
        // 99.99999… ms, which a cut to whole milliseconds read as 99 ms (and another hash).
        static string Chart(string measure)
            => "BPM:150\nCOURSE:Oni\n#START\n" + string.Concat(Enumerable.Repeat(measure + ",\n", 40)) + "#END\n";
        var sixteen = TjaParser.Parse(Chart("1111111111111111"));
        var fortyEight = TjaParser.Parse(Chart(string.Concat(Enumerable.Repeat("100", 16))));
        Assert.Equal(sixteen.Notes.Select(n => n.Time), fortyEight.Notes.Select(n => n.Time));
        Assert.Equal(sixteen.Bars.Select(n => n.Time), fortyEight.Bars.Select(n => n.Time));
        Assert.Equal(0.1, fortyEight.Notes[1].Time);
        Assert.Equal(ChartHash.Of(sixteen), ChartHash.Of(fortyEight));
    }

    [Fact]
    public void TimesAreTheNearestThousandthOfAMillisecond()
    {
        // 160 BPM: a 64th is 23.4375 ms, half way between two points of the grid: away from zero.
        var chart = TjaParser.Parse("BPM:160\nCOURSE:Oni\n#START\n" + "11" + new string('0', 62) + ",\n#END\n");
        Assert.Equal(new[] { 0, 0.023438 }, chart.Notes.Select(n => n.Time));
        // A third of a 120 BPM measure is 666.666… ms.
        chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n111,\n#END\n");
        Assert.Equal(new[] { 0, 0.666667, 1.333333 }, chart.Notes.Select(n => n.Time));
    }

    [Fact]
    public void EveryTimeIsWithinHalfAGridStepOfItsUnroundedTime()
    {
        int notes = 0;
        foreach (var (name, text) in GoldenTests.Corpus())
        {
            List<ChartBlockHash> blocks;
            try { blocks = ChartHash.OfFile(text).ToList(); }
            catch (FormatException) { continue; }
            foreach (var chart in blocks.Where(b => b.Chart != null).Select(b => b.Chart))
                foreach (var note in chart.Notes.Concat(chart.Bars))
                {
                    double unrounded = note.RawMs - chart.Offset * 1000;
                    Assert.True(Math.Abs(note.Time * 1000 - unrounded) <= 0.0005 + 1e-7, $"{name}: {note.Time} for {unrounded} ms");
                    notes++;
                }
        }
        Assert.True(notes > 10000, $"{notes} notes and bar lines");
    }

    [Fact]
    public void OffsetIsNotRoundedToAWholeMillisecond()
    {
        var chart = TjaParser.Parse("BPM:120\nOFFSET:-1.2345\nCOURSE:Oni\n#START\n1010,\n#END\n");
        Assert.Equal(new[] { 1.2345, 2.2345 }, chart.Notes.Select(n => n.Time));
        // The beat clock runs from the same OFFSET, read at the play clock's whole millisecond:
        // half a millisecond before the first note, and half after.
        var clocks = new ScrollClockTimeline(chart);
        Assert.Equal(-0.5 * 120 / 15000, clocks.At16(BranchRoute.Normal, 1.2345), 9);
        Assert.Equal(0.5 * 120 / 15000, clocks.At16(BranchRoute.Normal, 1.235), 9);
    }

    [Fact]
    public void TheParsedChartStaysOnTheBlockHash()
    {
        var blocks = ChartHash.OfFile(Tja.Sample);
        Assert.All(blocks, b => Assert.NotNull(b.Chart));
        Assert.Equal(blocks[0].Hash, ChartHash.Of(blocks[0].Chart));
    }
}
