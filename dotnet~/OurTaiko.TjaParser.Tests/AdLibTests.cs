using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// OpenTaiko's F (Ad-Lib): a hidden note of its own, with no text.
public class AdLibTests
{
    static TaikoChart Parse(string body) => TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n" + body + "#END\n");

    [Fact]
    public void FIsAnAdLib()
    {
        var chart = Parse("1F2F,\n");
        Assert.Equal(new[] { NoteKind.Don, NoteKind.AdLib, NoteKind.Ka, NoteKind.AdLib }, chart.Notes.Select(n => n.Kind));
        var adLib = chart.Notes[1];
        Assert.True(adLib.IsAdLib);
        Assert.False(adLib.IsLong);
        Assert.False(adLib.IsKa);
        Assert.False(adLib.IsBigNote);
        Assert.Equal(0.5, adLib.Time);
    }

    [Fact]
    public void AnAdLibHasNoText()
    {
        Assert.All(Parse("FFFF,\n1F,\n").Notes.Where(n => n.IsAdLib), n => Assert.Equal(NoteMoji.None, n.Moji));
    }

    // Hidden, it neither breaks nor joins a stream: ドコドン stays with an Ad-Lib among the dons.
    [Fact]
    public void AnAdLibLeavesTheTextAroundItAlone()
    {
        var withAdLib = Parse("1F1010000000000000,\n").Notes.Where(n => !n.IsAdLib).Select(n => n.Moji);
        var without = Parse("1010100000000000,\n").Notes.Select(n => n.Moji);
        Assert.Equal(without, withAdLib);
        Assert.Equal(new[] { NoteMoji.DonShort, NoteMoji.Ko, NoteMoji.Don }, without);
    }

    [Fact]
    public void AnAdLibEndsAnOpenRollAndIsPlaced()
    {
        var chart = Parse("5000F000,\n");
        Assert.Equal(new[] { NoteKind.Roll, NoteKind.AdLib }, chart.Notes.Select(n => n.Kind));
        Assert.Equal(1.0, chart.Notes[0].EndTime);
        Assert.Contains(chart.Warnings, w => w.StartsWith("An unended roll is ended"));
    }

    [Fact]
    public void LowerCaseFIsNotRead()
    {
        Assert.Throws<NotSupportedException>(() => Parse("1f,\n"));
    }

    [Fact]
    public void AnAdLibHashesApartFromABlank()
    {
        Assert.NotEqual(ChartHash.Of(Parse("1000,\n")), ChartHash.Of(Parse("1F00,\n")));
    }
}
