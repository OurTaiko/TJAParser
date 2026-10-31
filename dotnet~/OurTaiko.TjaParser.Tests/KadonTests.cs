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

    // Only letters that are a NoteKind are read: A–F, H and later are not yet.
    [Theory]
    [InlineData("1A,\n")]
    [InlineData("1F,\n")]
    [InlineData("1H,\n")]
    [InlineData("1Z,\n")]
    public void OtherLettersAreNotRead(string body)
    {
        Assert.Throws<NotSupportedException>(() => Parse(body));
    }

    [Fact]
    public void AKadonHashesApartFromABigDon()
    {
        Assert.NotEqual(ChartHash.Of(Parse("3000,\n")), ChartHash.Of(Parse("G000,\n")));
    }

    // A real chart (PeepoDrumKit 2025-09-03): four Kadons an eighth apart at BPM 224, ending a 4/4 measure
    // before a 3/4 one, the line followed by a comment.
    [Fact]
    public void IrohaUtaReadsItsFourKadons()
    {
        string text = Tja.Fixture("IrohaUta.tja");
        var chart = TjaParser.Parse(text, "Oni");
        var kadons = chart.Notes.Where(n => n.IsKadon).ToList();
        Assert.Equal(4, kadons.Count);
        for (int i = 1; i < 4; i++) Assert.Equal(60.0 / 224 / 2, kadons[i].Time - kadons[i - 1].Time, 3);
        Assert.All(kadons, n => Assert.Equal(NoteMoji.Kadon, n.Moji));
        // The 3/4 measure after them starts an eighth after the last one, with a don.
        var next = chart.Notes.First(n => n.Time > kadons[3].Time);
        Assert.Equal(NoteKind.Don, next.Kind);
        Assert.Equal(60.0 / 224 / 2, next.Time - kadons[3].Time, 3);
        Assert.Empty(chart.Warnings);
        var block = Assert.Single(ChartHash.OfFile(text));
        Assert.Null(block.Error);
        Assert.StartsWith("v1:", block.Hash);
    }
}
