using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// OpenTaiko's C (mine), a drawn note not to hit, and E, which OpenTaiko lists as unused.
public class MineTests
{
    static TaikoChart Parse(string body) => TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n" + body + "#END\n");

    [Fact]
    public void CIsAMine()
    {
        var chart = Parse("1C2C,\n");
        Assert.Equal(new[] { NoteKind.Don, NoteKind.Mine, NoteKind.Ka, NoteKind.Mine }, chart.Notes.Select(n => n.Kind));
        var mine = chart.Notes[1];
        Assert.True(mine.IsMine);
        Assert.False(mine.IsLong);
        Assert.False(mine.IsKa);
        Assert.False(mine.IsBigNote);
        Assert.False(mine.IsAdLib);
        Assert.Equal(0.5, mine.Time);
    }

    [Fact]
    public void AMineHasNoText()
    {
        Assert.All(Parse("CCCC,\n1C,\n").Notes.Where(n => n.IsMine), n => Assert.Equal(NoteMoji.None, n.Moji));
    }

    // Drawn, a mine sits in a stream like any note: among dons it is not one, so no ドコドン.
    [Fact]
    public void AMineTakesItsPlaceInAStream()
    {
        var moji = Parse("1C1010000000000000,\n").Notes.Select(n => n.Moji);
        Assert.Equal(new[] { NoteMoji.DonShort, NoteMoji.None, NoteMoji.DonShort, NoteMoji.Don }, moji);
    }

    [Fact]
    public void AMineEndsAnOpenRollAndIsPlaced()
    {
        var chart = Parse("5000C000,\n");
        Assert.Equal(new[] { NoteKind.Roll, NoteKind.Mine }, chart.Notes.Select(n => n.Kind));
        Assert.Equal(1.0, chart.Notes[0].EndTime);
        Assert.Contains(chart.Warnings, w => w.StartsWith("An unended roll is ended"));
    }

    [Fact]
    public void LowerCaseCIsNotRead()
    {
        Assert.Throws<NotSupportedException>(() => Parse("1c,\n"));
    }

    [Fact]
    public void AMineHashesApartFromABlank()
    {
        Assert.NotEqual(ChartHash.Of(Parse("1000,\n")), ChartHash.Of(Parse("1C00,\n")));
    }

    // E keeps its slot and makes no note: the same chart as a 0, with a warning.
    [Fact]
    public void EIsABlank()
    {
        var withE = Parse("1E2E,\n");
        Assert.Equal(new[] { NoteKind.Don, NoteKind.Ka }, withE.Notes.Select(n => n.Kind));
        Assert.Equal(1.0, withE.Notes[1].Time);
        Assert.Contains(withE.Warnings, w => w.StartsWith("The unused note E is treated as a blank"));
        Assert.Equal(ChartHash.Of(Parse("1020,\n")), ChartHash.Of(withE));
    }

    // As OpenTaiko's unknown symbol: it ends an open roll where it stands, then is a blank.
    [Fact]
    public void EEndsAnOpenRoll()
    {
        var chart = Parse("5000E000,\n");
        Assert.Equal(new[] { NoteKind.Roll }, chart.Notes.Select(n => n.Kind));
        Assert.Equal(1.0, chart.Notes[0].EndTime);
        Assert.Contains(chart.Warnings, w => w.StartsWith("An unended roll is ended"));
    }

    [Fact]
    public void LowerCaseEIsNotRead()
    {
        Assert.Throws<NotSupportedException>(() => Parse("1e,\n"));
    }
}
