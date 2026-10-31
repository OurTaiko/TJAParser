using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// OpenTaiko's G (Kadon, the purple note): read as a note of its own, with its own text, カドン.
public class KadonTests
{
    static TaikoChart Parse(string body) => TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n" + body + "#END\n");

    [Fact]
    public void GIsAKadon()
    {
        var chart = Parse("1G2G,\n");
        Assert.Equal(new[] { NoteKind.Don, NoteKind.Kadon, NoteKind.Ka, NoteKind.Kadon }, chart.Notes.Select(n => n.Kind));
        var kadon = chart.Notes[1];
        Assert.True(kadon.IsKadon);
        Assert.False(kadon.IsLong);
        Assert.False(kadon.IsBigNote);
        Assert.Equal(0.5, kadon.Time);
    }

    [Fact]
    public void AKadonHasItsOwnText()
    {
        Assert.All(Parse("GGGG,\n1G,\n").Notes.Where(n => n.IsKadon), n => Assert.Equal(NoteMoji.Kadon, n.Moji));
    }

    [Fact]
    public void AKadonEndsAnOpenRollAndIsPlaced()
    {
        var chart = Parse("5000G000,\n");
        Assert.Equal(new[] { NoteKind.Roll, NoteKind.Kadon }, chart.Notes.Select(n => n.Kind));
        Assert.Equal(1.0, chart.Notes[0].EndTime);
        Assert.Contains(chart.Warnings, w => w.StartsWith("An unended roll is ended"));
    }

    [Fact]
    public void LowerCaseGIsNotRead()
    {
        // OpenTaiko's note letters are upper case only.
        Assert.Throws<NotSupportedException>(() => Parse("1g,\n"));
    }

    [Fact]
    public void AKadonHashesApartFromABigDon()
    {
        Assert.NotEqual(ChartHash.Of(Parse("3000,\n")), ChartHash.Of(Parse("G000,\n")));
    }
}
