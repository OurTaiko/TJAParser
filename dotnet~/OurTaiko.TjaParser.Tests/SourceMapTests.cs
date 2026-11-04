using OurTaiko;
using OurTaiko.Corpus;
using Xunit;

namespace OurTaiko.Tests;

// I0-4 (Documentation~/Parser-TODO.md): an analysis knows where each part of its chart is written,
// and which note is written where. Checked over the golden corpus and more mutated charts.
public class SourceMapTests
{
    const string NoteCharacters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    static IEnumerable<string> Texts()
    {
        foreach (var (_, text) in GoldenTests.Corpus()) yield return text;
        for (int i = 0; i < 300; i++) yield return ChartCorpus.Mutate(ChartCorpus.Generate(12000 + i % 60, 10, messy: true), 13000 + i);
    }

    static string Key(string text, TextSpan span)
    {
        string cut = text.Substring(span.Start, span.Length);
        int space = cut.IndexOfAny(new[] { ' ', '\t' });
        return (space < 0 ? cut : cut.Substring(0, space)).ToUpperInvariant();
    }

    [Fact]
    public void EveryPartOfTheChartIsWhereTheMapSays()
    {
        int notes = 0;
        foreach (string text in Texts())
            foreach (string course in SongInfo.CourseNames)
            {
                var analysis = TjaParser.Analyze(text, course);
                var chart = analysis.Chart;
                if (chart == null) continue;
                var map = analysis.SourceMap;
                foreach (var note in chart.Notes)
                {
                    Assert.True(map.TryGetSpan(note, out var span));
                    Assert.Equal(NoteCharacters[(int)note.Kind], text[span.Start]);
                    Assert.Same(note, map.NoteAt(span.Start, out bool tail));
                    Assert.False(tail);
                    if (note.IsLong)
                    {
                        Assert.True(map.TryGetTailSpan(note, out var end), "a roll without its end");
                        // An 8, a note that ends it, #END, or where a missing #END would be.
                        Assert.True(end.Length == 0 || text[end.Start] == '8' || NoteCharacters.IndexOf(text[end.Start]) > 0 || Key(text, end) == "#END",
                            $"a roll ended at '{text.Substring(end.Start, end.Length)}'");
                        if (end.Length > 0 && text[end.Start] == '8')
                        {
                            Assert.Same(note, map.NoteAt(end.Start, out tail));
                            Assert.True(tail);
                        }
                    }
                    notes++;
                }
                // A bar line's measure ends at its comma, except where a measure had none (each said).
                int withoutComma = 0;
                foreach (var bar in chart.Bars)
                {
                    Assert.True(map.TryGetSpan(bar, out var span));
                    Assert.True(span.Length > 0);
                    if (text[span.End - 1] != ',') withoutComma++;
                }
                Assert.True(withoutComma <= analysis.Diagnostics.Count(d => d.Kind == TjaDiagnosticKind.LastMeasureWithoutComma || d.Kind == TjaDiagnosticKind.MissingCommaBeforeBranch));
                foreach (var tempo in chart.Tempos) { map.TryGetSpan(tempo, out var s); Assert.Contains(Key(text, s), new[] { "#BPMCHANGE", "#DELAY" }); }
                foreach (var gogo in chart.Gogos) { map.TryGetSpan(gogo, out var s); Assert.Equal(gogo.On ? "#GOGOSTART" : "#GOGOEND", Key(text, s)); }
                foreach (var section in chart.Sections) { map.TryGetSpan(section, out var s); Assert.Equal("#SECTION", Key(text, s)); }
                foreach (var move in chart.JudgeMoves) { map.TryGetSpan(move, out var s); Assert.Equal("#JPOSSCROLL", Key(text, s)); }
                foreach (var alpha in chart.LaneAlphas) { map.TryGetSpan(alpha, out var s); Assert.Equal("#LANE_ALPHA", Key(text, s)); }
                foreach (var branch in chart.Branches) { map.TryGetSpan(branch, out var s); Assert.Equal("#BRANCHSTART", Key(text, s)); }
            }
        Assert.True(notes > 50000, notes.ToString());
    }

    [Fact]
    public void ABarLineIsItsMeasureAndAStretchWithoutANoteHasNone()
    {
        string text = "BPM:120\nCOURSE:Oni\n#START\n#SCROLL 2\n1020,\n,\n7[5]0\n08,\n#END\n";
        var a = TjaParser.Analyze(text);
        var map = a.SourceMap;
        Assert.Equal(new[] { "1020,", ",", "7[5]0\n08," }, a.Chart.Bars.Select(b => { map.TryGetSpan(b, out var s); return text.Substring(s.Start, s.Length); }));
        // The balloon's inline count is part of it; the blanks and the command are no note.
        Assert.Same(a.Chart.Notes[2], map.NoteAt(text.IndexOf("[5]") + 1));
        Assert.Null(map.NoteAt(text.IndexOf("1020") + 1));
        Assert.Null(map.NoteAt(text.IndexOf("#SCROLL")));
        Assert.True(map.NoteAt(text.IndexOf("08,") + 1, out bool tail) == a.Chart.Notes[2] && tail);
    }
}
