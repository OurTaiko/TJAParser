using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

public class RawTimeTests
{
    [Fact]
    public void RawMsIsTheUnroundedTimeFromStartWithDelays()
    {
        // 170 BPM sixteenths are 88.235… ms: Time is cut to whole milliseconds (after OFFSET),
        // RawMs is not; #DELAY moves both; OFFSET moves only Time.
        var chart = TjaParser.Parse("BPM:170\nOFFSET:-1\nCOURSE:Oni\n#START\n1100000000000000,\n#DELAY 0.5\n1,\n#END\n");
        double sixteenth = 15000.0 / 170;
        Assert.Equal(new[] { 0, sixteenth, 4 * 4 * sixteenth + 500 }, chart.Notes.Select(n => n.RawMs));
        Assert.Equal(new[] { 1.0, 1.088, 2.911 }, chart.Notes.Select(n => n.Time));
    }

    [Fact]
    public void TheParsedChartStaysOnTheBlockHash()
    {
        var blocks = ChartHash.OfFile(Tja.Sample);
        Assert.All(blocks, b => Assert.NotNull(b.Chart));
        Assert.Equal(blocks[0].Hash, ChartHash.Of(blocks[0].Chart));
    }
}
