using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// I3-2 (Documentation~/Parser-TODO.md): formatting changes nothing played: every block's hash is
// the same after, and formatting again changes nothing more.
public class FormatTests
{
    static string Format(string text, int resolution) => TjaTextEdit.Apply(text, TjaFormat.Edits(new TjaDocument(text), resolution));

    [Fact]
    public void EveryBlockPlaysTheSameFormatted()
    {
        int changed = 0;
        foreach (var (name, text) in GoldenTests.Corpus().Take(300))
            foreach (int resolution in new[] { 0, 16, 48 })
            {
                string formatted = Format(text, resolution);
                if (formatted != text) changed++;
                foreach (string course in SongInfo.CourseNames)
                {
                    string before = Hash(text, course), after = Hash(formatted, course);
                    Assert.True(before == after, $"{name} {course} at {resolution}");
                }
                Assert.Equal(formatted, Format(formatted, resolution));
            }
        Assert.True(changed > 100, changed.ToString());
    }

    static string Hash(string text, string course)
    {
        try { return ChartHash.Of(TjaParser.Parse(text, course)); }
        catch (Exception e) when (e is FormatException || e is NotSupportedException) { return "error"; }
    }

    [Fact]
    public void CapitalsAndAResolution()
    {
        string text = "bpm:120\ncourse:Oni\n#START\n#scroll 2\n1020,\n1000000020000000,\n10200,\n#measure 3/4\n102,\n#END\n";
        Assert.Equal("BPM:120\nCOURSE:Oni\n#START\n#SCROLL 2\n1020,\n1000000020000000,\n10200,\n#MEASURE 3/4\n102,\n#END\n", Format(text, 0));
        // In sixteenths: 1020 grows, the sixteenths stay, 10200 (fifths) cannot; 3/4 takes 12.
        Assert.Equal("BPM:120\nCOURSE:Oni\n#START\n#SCROLL 2\n1000000020000000,\n1000000020000000,\n10200,\n#MEASURE 3/4\n100000002000,\n#END\n", Format(text, 16));
        // In quarters: the sixteenths with notes on quarters only shrink.
        Assert.Equal("BPM:120\nCOURSE:Oni\n#START\n#SCROLL 2\n1020,\n1020,\n10200,\n#MEASURE 3/4\n102,\n#END\n", Format(text, 4));
    }
}
