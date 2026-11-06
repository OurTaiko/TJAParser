using System.Text;
using OurTaiko;
using OurTaiko.Corpus;
using Xunit;

namespace OurTaiko.Tests;

// I0-3 (Documentation~/Parser-TODO.md): TjaParser.Analyze reads on after an error. Whatever the
// text, it does not throw; where Parse succeeds it gives the same chart, and where Parse throws its
// first error is that exception, word for word.
public class RecoveryTests
{
    static string Block(string body) => "BPM:120\nCOURSE:Oni\n#START\n" + body + "#END\n";

    static IEnumerable<string> Texts()
    {
        foreach (var (_, text) in GoldenTests.Corpus()) yield return text;
        for (int i = 0; i < 2000; i++) yield return ChartCorpus.Mutate(ChartCorpus.Generate(9000 + i % 120, 8, messy: i % 3 != 0), 11000 + i);
    }

    [Fact]
    public void AnalyzeAgreesWithParseAndNeverThrows()
    {
        int failed = 0, parsed = 0;
        foreach (string text in Texts())
            foreach (string course in SongInfo.CourseNames)
            {
                TaikoChart chart = null;
                Exception error = null;
                try { chart = TjaParser.Parse(text, course); }
                catch (Exception e) when (e is FormatException || e is NotSupportedException) { error = e; }
                var analysis = TjaParser.Analyze(text, course);
                foreach (var d in analysis.Diagnostics)
                    Assert.True(d.Span.Start >= 0 && d.Span.End <= text.Length, d.ToString());
                if (error == null)
                {
                    Assert.False(analysis.HasErrors, string.Join("\n", analysis.Diagnostics));
                    Assert.Equal(ChartHash.Of(chart), ChartHash.Of(analysis.Chart));
                    Assert.Equal(chart.Warnings, analysis.Chart.Warnings);
                    parsed++;
                }
                else
                {
                    var first = analysis.Diagnostics.First(d => d.Severity == TjaSeverity.Error);
                    Assert.Equal(error.Message, first.Message);
                    Assert.Equal(error is NotSupportedException, first.Kind == TjaDiagnosticKind.UnsupportedNote);
                    failed++;
                }
            }
        // Both sides are well exercised.
        Assert.True(parsed > 2000 && failed > 2000, $"{parsed} parsed, {failed} failed");
    }

    [Fact]
    public void ANoteItDoesNotKnowIsABlank()
    {
        // It still takes its slot: the second don is at 2/3 of the two-second measure.
        var a = TjaParser.Analyze(Block("1H1,\n"));
        Assert.Equal(new[] { 0, 1.333333 }, a.Chart.Notes.Select(n => n.Time));
        Assert.Equal(TjaDiagnosticKind.UnsupportedNote, Assert.Single(a.Diagnostics).Kind);
    }

    [Fact]
    public void ABadBpmChangeOrMeasureIsDropped()
    {
        var a = TjaParser.Analyze(Block("#BPMCHANGE x\n#MEASURE 3\n#BPMCHANGE -1\n#MEASURE 0/4\n1,\n1,\n"));
        Assert.Equal(new[] { 0, 2.0 }, a.Chart.Notes.Select(n => n.Time));
        Assert.Equal(new[] { TjaDiagnosticKind.NumberUnreadable, TjaDiagnosticKind.MeasureWithoutSlash, TjaDiagnosticKind.InvalidBpmChange, TjaDiagnosticKind.InvalidMeasure },
            a.Diagnostics.Select(d => d.Kind));
    }

    [Fact]
    public void BadHeadersKeepTheirDefaults()
    {
        var a = TjaParser.Analyze("BPM:x\nOFFSET:y\nCOURSE:Oni\nLEVEL:z\n#START\n1,\n#END\n");
        Assert.Equal((120, 0, 0), (a.Chart.Bpm, a.Chart.Offset, a.Chart.Level));
        Assert.Equal(3, a.Diagnostics.Count(d => d.Kind == TjaDiagnosticKind.NumberUnreadable));
        Assert.Equal(120, TjaParser.Analyze("BPM:0\nCOURSE:Oni\n#START\n1,\n#END\n").Chart.Bpm);
    }

    [Fact]
    public void BadBracketsAreDropped()
    {
        // An unclosed [ drops the rest of its line; a [ after no note drops up to its ].
        Assert.Equal(new[] { NoteKind.Don, NoteKind.Ka }, TjaParser.Analyze(Block("1[5 22\n2,\n")).Chart.Notes.Select(n => n.Kind));
        Assert.Equal(new[] { NoteKind.Don, NoteKind.Don }, TjaParser.Analyze(Block("1,[5]1,\n")).Chart.Notes.Select(n => n.Kind));
        // A count on a note that is not a balloon, or one that does not read, is ignored.
        var counts = TjaParser.Analyze("BPM:120\nBALLOON:4\nCOURSE:Oni\n#START\n1[3]7[0]008,\n#END\n");
        Assert.Equal(new[] { 0, 4 }, counts.Chart.Notes.Select(n => n.BalloonHits));
    }

    [Fact]
    public void AMissingCommaBeforeABranchEndsTheMeasure()
    {
        var a = TjaParser.Analyze(Block("11\n#BRANCHSTART p,1,2\n#N\n1,\n#E\n2,\n#M\n3,\n#BRANCHEND\n"));
        Assert.Equal(TjaDiagnosticKind.MissingCommaBeforeBranch, Assert.Single(a.Diagnostics).Kind);
        Assert.Equal(new[] { 0, 1.0, 2, 2, 2 }, a.Chart.Notes.Select(n => n.Time));
    }

    [Fact]
    public void MisplacedRouteMarkersAreIgnored()
    {
        var a = TjaParser.Analyze(Block("#N\n#BRANCHEND\n1,\n#BRANCHSTART p,1,2\n#N\n1,\n#N\n2,\n#BRANCHEND\n"));
        Assert.Equal(new[] { TjaDiagnosticKind.RouteWithoutBranch, TjaDiagnosticKind.BranchEndWithoutStart, TjaDiagnosticKind.DuplicateRoute },
            a.Diagnostics.Where(d => d.Severity == TjaSeverity.Error).Select(d => d.Kind));
        // The second #N's notes go on the normal route after its first measure.
        Assert.Equal(new[] { NoteKind.Don, NoteKind.Ka }, a.Chart.NoteLists[1].Where(e => !e.IsBar).Select(e => e.Note.Kind));
    }

    [Fact]
    public void NoBlockNoChart()
    {
        Assert.Null(TjaParser.Analyze("  ").Chart);
        Assert.Null(TjaParser.Analyze(Block("1,\n"), "Easy").Chart);
        Assert.Throws<ArgumentException>(() => TjaParser.Analyze(Block("1,\n"), "Oni", "p3"));
    }
}
