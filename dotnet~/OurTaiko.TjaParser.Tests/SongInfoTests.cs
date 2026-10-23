using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// What the servers register a chart from, and what the game lists: one block list, the header as written.
public class SongInfoTests
{
    const string Text = "TITLE:Song\nTITLEJP:うた\nSUBTITLE:--Someone\nSUBTITLEZH_TW:--某人\nMAKER:Maker\nNOTESDESIGNER3:Oni Author\nBPM:120,5\nOFFSET:-1.5\nWAVE:a.ogg\nBGMOVIE:a.mp4\nMOVIEOFFSET:0.25\nDEMOSTART:10\n"
        + "COURSE:Oni\nLEVEL:10\n#START\n1,\n#END\n"
        + "COURSE:Ura\nLEVEL:10\nSTYLE:Double\n#START P1\n1,\n#END\n#START P2\n#BRANCHSTART p,50,80\n#N\n2,\n#E\n2,\n#M\n2,\n#BRANCHEND\n#END\n"
        + "COURSE:0\n#START\n\n#END\n";

    [Fact]
    public void EveryBlockIsListedInFileOrder()
    {
        var info = SongInfo.Read(Text);
        Assert.Equal(new[] { "Oni", "Ura", "Ura", "0" }, info.Blocks.Select(b => b.Course));
        Assert.Equal(new[] { Difficulty.Oni, Difficulty.Ura, Difficulty.Ura, Difficulty.Easy }, info.Blocks.Select(b => b.Difficulty));
        Assert.Equal(new[] { "single", "p1", "p2", "single" }, info.Blocks.Select(b => b.Side));
        Assert.Equal(new[] { "", "P1", "P2", "" }, info.Blocks.Select(b => b.Player));
        Assert.Equal(new[] { 15, 21, 24, 35 }, info.Blocks.Select(b => b.Line));
        // Block(difficulty, side) finds one block; which side a seat plays is the game's rule, not here.
        Assert.Equal(24, info.Block(Difficulty.Ura, "p2").Line);
        Assert.Null(info.Block(Difficulty.Ura, "single"));
    }

    [Fact]
    public void BlocksCarryWhatTheServersValidate()
    {
        var info = SongInfo.Read(Text);
        Assert.Equal(new[] { 10, 10, 10, 0 }, info.Blocks.Select(b => b.Level));
        Assert.Equal(new[] { "", "Double", "Double", "" }, info.Blocks.Select(b => b.Style));
        Assert.Equal(new[] { true, true, true, false }, info.Blocks.Select(b => b.HasNotes));
        Assert.Equal(new[] { false, false, true, false }, info.Blocks.Select(b => b.IsBranching));
        // NOTESDESIGNERn credits its course; the others get MAKER.
        Assert.Equal(new[] { "Oni Author", "Maker", "Maker", "Maker" }, info.Blocks.Select(b => b.Maker));
        Assert.Equal("Oni Author", info.NotesDesigners["Oni"]);
    }

    [Theory]
    [InlineData("COURSE:Oni\n#START\n1,\n#END\nCOURSE:Tower\n#START\n1,\n#END\n", "Unsupported COURSE 'Tower' (line 6)")]
    [InlineData("COURSE:Dan\nCOURSE:Oni\n#START\n1,\n#END\n", "Unsupported COURSE 'Dan' (line 2)")]
    [InlineData("COURSE:Oni\n#START\n1,\n#END\nCOURSE:3\n#START\n2,\n#END\n", "Oni/single is written twice (lines 3 and 7)")]
    [InlineData("COURSE:Oni\n#START P1\n1,\n#END\n#START P1\n2,\n#END\n", "Oni/p1 is written twice (lines 3 and 6)")]
    [InlineData("COURSE:Oni\n#START P3\n1,\n#END\n", "#START takes P1 or P2, not 'P3' (line 3)")]
    public void UnsupportedCoursesRepeatedSlotsAndOddPlayersAreErrors(string body, string message)
    {
        var error = Assert.Throws<FormatException>(() => SongInfo.Read("BPM:120\n" + body));
        Assert.StartsWith(message, error.Message);
        Assert.Throws<FormatException>(() => ChartHash.OfFile("BPM:120\n" + body));
    }

    [Fact]
    public void TitlesAreKeptAsWrittenAndPickedByLanguage()
    {
        var info = SongInfo.Read(Text);
        Assert.Equal(new Dictionary<string, string> { ["origin"] = "Song", ["ja"] = "うた" }, info.Titles);
        Assert.Equal(new Dictionary<string, string> { ["origin"] = "--Someone", ["zh_tw"] = "--某人" }, info.Subtitles);
        Assert.Null(info.Language);
        Assert.Equal("Song", info.Title);
        Assert.Equal("Someone", info.Subtitle);
        Assert.Equal("うた", info.DisplayTitle("ja"));
        Assert.Equal("Song", info.DisplayTitle("ko"));
        Assert.Equal("某人", info.DisplaySubtitle("zh_tw"));
        var ja = SongInfo.Read(Text, "ja");
        Assert.Equal("ja", ja.Language);
        Assert.Equal("うた", ja.Title);
        Assert.Equal("Someone", ja.Subtitle);
        // A blank translation falls back to the origin; no TITLE at all is "Untitled".
        Assert.Equal("Song", SongInfo.Read(Text.Replace("TITLEJP:うた", "TITLEJP:  "), "ja").Title);
        Assert.Equal("Untitled", SongInfo.Read("TITLEJA:日本語\nBPM:120\n", "en").Title);
        Assert.Equal("日本語", SongInfo.Read("TITLEJA:日本語\nBPM:120\n", "ja").Title);
    }

    [Fact]
    public void HeadersBeyondTheTitles()
    {
        var info = SongInfo.Read(Text);
        Assert.Equal(120.5, info.Bpm);
        Assert.Equal(-1.5, info.Offset);
        Assert.Equal(10, info.DemoStart);
        Assert.Equal("a.ogg", info.Wave);
        Assert.Equal("a.mp4", info.Movie);
        Assert.Equal(0.25, info.MovieOffset);
        Assert.Equal("Maker", info.Maker);
    }

    [Fact]
    public void TjaLinesNumberTheLines()
    {
        var lines = TjaLines.Read("TITLE:x\n\n// c\nCOURSE:Oni\n#START\n1,\nCOURSE:Edit\n#START\n2,\n#END\n").ToList();
        Assert.Equal(new[] { 1, 4, 5, 6, 7, 7, 8, 9, 10 }, lines.Select(l => l.Line));
        Assert.True(lines[4].Implicit);
    }
}
