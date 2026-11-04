using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// I2-2 (Documentation~/Parser-TODO.md): the editor's checks (TJA3xxx), each on what it is about.
// They are TjaDocument.Diagnostics', not the parser's: Parse warns and throws as before.
public class LintTests
{
    static string Block(string body, string level = "LEVEL:9\n") => "BPM:120\nCOURSE:Oni\n" + level + "#START\n" + body + "#END\n";

    const string Branch = "#BRANCHSTART p,1,2\n";

    public static IEnumerable<object[]> Cases() => new[]
    {
        Case(TjaDiagnosticKind.UnsupportedCourse, "COURSE:Tower\nLEVEL:1\n#START\n1,\n#END\n", "Tower"),
        Case(TjaDiagnosticKind.BlockWrittenTwice, Block("1,\n") + "COURSE:3\nLEVEL:9\n#START\n1,\n#END\n", "#START", 2),
        Case(TjaDiagnosticKind.StartPlayer, "COURSE:Oni\nLEVEL:1\n#START P3\n1,\n#END\n", "#START P3"),
        Case(TjaDiagnosticKind.LevelOutOfRange, Block("1,\n", "LEVEL:12\n"), "12", 2),
        Case(TjaDiagnosticKind.SimilarCommand, Block("#GOGOSTRAT\n1,\n"), "#GOGOSTRAT"),
        Case(TjaDiagnosticKind.UnusualSlots, Block("10000,\n1,\n"), "10000,"),
        Case(TjaDiagnosticKind.RoutesEndApart, Block(Branch + "#N\n1,\n#E\n1,\n1,\n#M\n1,\n#BRANCHEND\n"), "#BRANCHSTART p,1,2"),
    };

    static object[] Case(TjaDiagnosticKind kind, string text, string cut, int nth = 1) => new object[] { kind.Code, text, cut, nth };

    [Theory]
    [MemberData(nameof(Cases))]
    public void EachCheckIsReportedWhereItIs(string code, string text, string cut, int nth)
    {
        var diagnostics = new TjaDocument(text).Diagnostics();
        var found = diagnostics.Where(d => d.Code == code).ToList();
        Assert.True(found.Count == 1, $"{code}: {string.Join("; ", diagnostics)}");
        Assert.Equal(cut, text.Substring(found[0].Span.Start, found[0].Span.Length));
        // At the nth time the cut text is written.
        int at = -1;
        for (int i = 0; i < nth; i++) at = text.IndexOf(cut, at + 1, StringComparison.Ordinal);
        Assert.Equal(at, found[0].Span.Start);
        // Not the parser's: TjaParser.Parse says nothing of it (where it reads the block at all).
        try { Assert.DoesNotContain(TjaParser.Parse(text).Warnings, w => w.StartsWith(found[0].Message)); }
        catch (FormatException) { }
    }

    [Fact]
    public void EveryCheckHasACase()
    {
        var covered = Cases().Select(c => (string)c[0]).ToHashSet();
        Assert.All(TjaDiagnosticKind.All.Where(k => !k.FromParser), k => Assert.Contains(k.Code, covered));
    }

    [Fact]
    public void TheChecksSaySpecifically()
    {
        Assert.Equal("Did you mean #GOGOSTART?", Message(Block("#GOGOSTRAT\n1,\n"), TjaDiagnosticKind.SimilarCommand));
        Assert.Equal("5 slots in a measure make notes of 1/5: not a division by 2s and 3s.", Message(Block("10000,\n"), TjaDiagnosticKind.UnusualSlots));
        Assert.Equal("10 slots in a measure make notes of 1/13.33: not a division by 2s and 3s.", Message(Block("#MEASURE 3/4\n1000000000,\n"), TjaDiagnosticKind.UnusualSlots));
        Assert.Equal("LEVEL is missing: not 1 to 10.", Message(Block("1,\n", ""), TjaDiagnosticKind.LevelOutOfRange));
        Assert.Equal("The routes of branch 0 end at different times (N 2 s, E 4 s, M 2 s).",
            Message(Block(Branch + "#N\n1,\n#E\n1,\n1,\n#M\n1,\n#BRANCHEND\n"), TjaDiagnosticKind.RoutesEndApart));
        // Nothing to say of a usual chart: halves, thirds, sixteenths, 48ths; a near command only if near.
        // So are 31 sixteenths in a 31/16 measure, 14 in a 7/8 one, and any slots that only place rolls and their ends.
        string usual = Block("1,\n10,\n100,\n1000000000000000,\n" + new string('0', 48) + ",\n,\n#MEASURE 31/16\n" + new string('1', 31)
            + ",\n#MEASURE 7/8\n" + new string('2', 14) + ",\n#MEASURE 4/4\n5,\n00000000000008,\n50000000000008,\n#FOOBAR\n" + Branch + "#N\n1,\n#E\n2,\n#M\n3,\n#BRANCHEND\n");
        Assert.Empty(new TjaDocument(usual).Diagnostics().Where(d => !d.Kind.FromParser));
    }

    static string Message(string text, TjaDiagnosticKind kind) => new TjaDocument(text).Diagnostics().Single(d => d.Kind == kind).Message;
}
