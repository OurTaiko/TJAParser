using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// I0-2 (Documentation~/Parser-TODO.md): every kind of problem has a stable code, and an analysis
// reports it on the token at fault. TjaParser.Parse words it the same: an error is the exception
// it throws, a warning the text in TaikoChart.Warnings (with its line).
public class DiagnosticTests
{
    static string Block(string body) => "BPM:120\nCOURSE:Oni\n#START\n" + body + "#END\n";

    const string Branch = "#BRANCHSTART p,1,2\n";

    // Kind, file, course asked for, and the text the diagnostic's span cuts out.
    public static IEnumerable<object[]> Cases() => new[]
    {
        Case(TjaDiagnosticKind.EmptyChart, "  \n", ""),
        Case(TjaDiagnosticKind.CourseNotFound, "BPM:120\nCOURSE:Easy\n#START\n1,\n#END\n", ""),
        Case(TjaDiagnosticKind.NumberUnreadable, "BPM:x\nCOURSE:Oni\n#START\n1,\n#END\n", "x"),
        Case(TjaDiagnosticKind.BpmNotPositive, "BPM: -5 \nCOURSE:Oni\n#START\n1,\n#END\n", "-5"),
        Case(TjaDiagnosticKind.UnclosedBracket, Block("107[5,\n"), "[5,"),
        Case(TjaDiagnosticKind.BracketWithoutNote, Block("1,[5]\n"), "[5]"),
        Case(TjaDiagnosticKind.MissingCommaBeforeBranch, Block("1\n" + Branch + "#N\n1,\n#BRANCHEND\n"), "#BRANCHSTART p,1,2"),
        Case(TjaDiagnosticKind.UnsupportedNote, Block("1H0,\n"), "H"),
        Case(TjaDiagnosticKind.CountOnNonBalloon, Block("1[5]0,\n"), "1[5]"),
        Case(TjaDiagnosticKind.InvalidBalloonCount, Block("7[0]8,\n"), "7[0]"),
        Case(TjaDiagnosticKind.InvalidBpmChange, Block("#BPMCHANGE 0\n1,\n"), "#BPMCHANGE 0"),
        Case(TjaDiagnosticKind.MeasureWithoutSlash, Block("#MEASURE 4\n1,\n"), "#MEASURE 4"),
        Case(TjaDiagnosticKind.InvalidMeasure, Block("#MEASURE 0/4\n1,\n"), "#MEASURE 0/4"),
        Case(TjaDiagnosticKind.BranchEndWithoutStart, Block("#BRANCHEND\n1,\n"), "#BRANCHEND"),
        Case(TjaDiagnosticKind.RouteWithoutBranch, Block("#N\n1,\n"), "#N"),
        Case(TjaDiagnosticKind.DuplicateRoute, Block(Branch + "#N\n1,\n#n\n1,\n#BRANCHEND\n"), "#n"),

        Case(TjaDiagnosticKind.EndMissing, "BPM:120\nCOURSE:Oni\n#START\n1,\nCOURSE:Easy\n", ""),
        Case(TjaDiagnosticKind.BalloonHeaderUnreadable, "BPM:120\nCOURSE:Oni\nBALLOON:1,x\n#START\n7008,\n#END\n", "BALLOON:1,x"),
        Case(TjaDiagnosticKind.LastMeasureWithoutComma, Block("1,\n12\n"), "2"),
        Case(TjaDiagnosticKind.RollEndedByEnd, Block("5000,\n"), "#END"),
        Case(TjaDiagnosticKind.SharedRollEndsDiffer, Block(Branch + "5,\n#N\n8,\n#E\n08,\n#M\n8,\n#BRANCHEND\n"), "8"),
        Case(TjaDiagnosticKind.BranchWithoutNormal, Block(Branch + "#E\n1,\n#BRANCHEND\n"), "#BRANCHEND"),
        Case(TjaDiagnosticKind.BranchConditionUnreadable, Block("#BRANCHSTART x,1,2\n#N\n1,\n#BRANCHEND\n"), "#BRANCHSTART x,1,2"),
        Case(TjaDiagnosticKind.CommonNotesInBranch, Block(Branch + "2,\n#N\n1,\n#BRANCHEND\n"), "2"),
        Case(TjaDiagnosticKind.UnknownCommand, Block("#FOO 1\n1,\n"), "#FOO 1"),
        Case(TjaDiagnosticKind.CommandDropped, Block("#SCROLL abc\n1,\n"), "#SCROLL abc"),
        Case(TjaDiagnosticKind.LaneAlphaBeforeDecision, Block(Branch + "#N\n#LANE_ALPHA 50 -5000\n1,\n#BRANCHEND\n"), "#LANE_ALPHA 50 -5000"),
        Case(TjaDiagnosticKind.StrayRollEnd, Block("1080,\n"), "8"),
        Case(TjaDiagnosticKind.RollHeadInRoll, Block("5060,\n8,\n"), "6"),
        Case(TjaDiagnosticKind.RollEndedByNote, Block("5001,\n"), "1"),
        Case(TjaDiagnosticKind.UnusedNoteE, Block("1E,\n"), "E"),
        Case(TjaDiagnosticKind.BalloonWithoutCount, Block("7008,\n"), "7"),
    };

    static object[] Case(TjaDiagnosticKind kind, string text, string cut) => new object[] { kind.Code, text, cut };

    [Theory]
    [MemberData(nameof(Cases))]
    public void EachKindIsReportedOnTheTokenAtFault(string code, string text, string cut)
    {
        var analysis = TjaParser.Analyze(text);
        var found = analysis.Diagnostics.FirstOrDefault(d => d.Code == code);
        Assert.True(found != null, $"{code} not among {string.Join(", ", analysis.Diagnostics)}");
        Assert.Equal(cut, text.Substring(found.Span.Start, found.Span.Length));
        if (found.Line > 0) Assert.Equal(found.Line, analysis.Lines.Line(found.Span.Start));

        // TjaParser.Parse says the same.
        if (found.Severity == TjaSeverity.Error)
        {
            var e = Assert.ThrowsAny<Exception>(() => TjaParser.Parse(text));
            Assert.Equal(found.Kind == TjaDiagnosticKind.UnsupportedNote ? typeof(NotSupportedException) : typeof(FormatException), e.GetType());
            Assert.Equal(found.Message, e.Message);
            Assert.Same(found, analysis.Diagnostics.First(d => d.Severity == TjaSeverity.Error));
        }
        else Assert.Contains(TjaParser.AtLine(found.Message, found.Line), TjaParser.Parse(text).Warnings);
    }

    [Fact]
    public void EveryKindHasACase()
    {
        // The parser's kinds; the editor's checks (TJA3xxx) are LintTests'.
        var covered = Cases().Select(c => (string)c[0]).ToHashSet();
        Assert.All(TjaDiagnosticKind.All.Where(k => k.FromParser), k => Assert.Contains(k.Code, covered));
        Assert.Equal(TjaDiagnosticKind.All.Count, TjaDiagnosticKind.All.Select(k => k.Code).Distinct().Count());
    }

    // Each occurrence is a diagnostic, though TaikoChart.Warnings keeps the text once.
    [Fact]
    public void EveryOccurrenceIsReported()
    {
        string text = Block("#FOO\n1,\n#FOO\n1,\n");
        Assert.Equal(new[] { 4, 6 }, TjaParser.Analyze(text).Diagnostics.Select(d => d.Line));
        Assert.Equal(new[] { "#FOO (line 4)" }, TjaParser.Parse(text).Warnings);
    }
}
