using System.Text;
using OurTaiko;
using OurTaiko.Corpus;
using Xunit;

namespace OurTaiko.Tests;

// I1-1 (Documentation~/Parser-TODO.md): a document reuses, after an edit, the analyses of the
// blocks the edit did not touch. Over random edit sequences every block's analysis equals a fresh
// TjaParser.Analyze of the new text, everything in it compared.
public class DocumentTests
{
    static readonly string[] Snippets = { "\n", "1,\n", "#SCROLL 2\n", "#END\n", "COURSE:Oni\n", "BALLOON:3\n", "/*", "*/", "LEVEL:5\n", " ", "#START\n", "7", "8" };
    static readonly string[] Notes = { "1", "2", "0", ",", "5", "8" };

    static string Edit(string text, Random r)
    {
        int op = r.Next(10);
        if (op < 6)
        {
            // In a note line: what typing in a block mostly is.
            var lines = new List<int>();
            for (int i = 0, start = 0; i <= text.Length; i++)
                if (i == text.Length || text[i] == '\n')
                {
                    if (i > start && char.IsDigit(text[start])) lines.Add(start + r.Next(i - start));
                    start = i + 1;
                }
            if (lines.Count > 0)
            {
                int at = lines[r.Next(lines.Count)];
                return r.Next(3) == 0 && at < text.Length && text[at] != '\n' ? text.Remove(at, 1) : text.Insert(at, Notes[r.Next(Notes.Length)]);
            }
        }
        int pos = r.Next(text.Length + 1);
        if (op < 8) return text.Insert(pos, Snippets[r.Next(Snippets.Length)]);
        return text.Remove(pos, Math.Min(text.Length - pos, 1 + r.Next(5)));
    }

    [Fact]
    public void EditedDocumentsReadAsAFreshAnalysis()
    {
        var r = new Random(5);
        int reused = 0, fresh = 0;
        var texts = new[] { Tja.Fixture("Donkama2000.tja"), Tja.Fixture("IrohaUta.tja"), Tja.Sample, Tja.Branching }
            .Concat(Enumerable.Range(0, 36).Select(i => ChartCorpus.Generate(18000 + i, 10, messy: i % 2 == 1)));
        foreach (string start in texts)
        {
            var doc = new TjaDocument(start);
            for (int version = 1; version <= 25; version++)
            {
                doc = doc.WithText(Edit(doc.Text, r), version);
                var keys = doc.Blocks.Select(b => (b.Course, b.Side)).Distinct().Append(("Oni", "single")).ToList();
                // Sometimes only some blocks are asked for: the others' analyses wait for a later version.
                foreach (var (course, side) in keys)
                {
                    if (r.Next(3) == 0) continue;
                    Assert.Equal(Dump(TjaParser.Analyze(doc.Text, course, side)), Dump(doc.Analyze(course, side)));
                }
                reused += doc.Reused; fresh += doc.Fresh;
            }
        }
        // Random edits change headers and block structure too; still, many analyses are moved.
        Assert.True(reused > 500, $"{reused} reused, {fresh} read afresh");
    }

    // Typing in a block reads that block again and moves every other.
    [Fact]
    public void TypingInABlockReadsOnlyThatBlock()
    {
        var r = new Random(1);
        for (int i = 0; i < 10; i++)
        {
            var doc = new TjaDocument(ChartCorpus.Generate(30000 + i, 20, messy: false, courses: 5));
            var keys = doc.Blocks.Select(b => (b.Course, b.Side)).Distinct().ToList();
            foreach (var (course, side) in keys) doc.Analyze(course, side);
            for (int version = 1; version <= 10; version++)
            {
                string text = doc.Text;
                var noteLines = text.Split('\n').Select((l, n) => (l, n)).Where(x => x.l.Length > 0 && char.IsDigit(x.l[0])).ToList();
                var (line, number) = noteLines[r.Next(noteLines.Count)];
                int at = text.Split('\n').Take(number).Sum(l => l.Length + 1) + r.Next(line.Length);
                doc = doc.WithChange(at, 0, "1", version);
                foreach (var (course, side) in keys)
                    Assert.Equal(Dump(TjaParser.Analyze(doc.Text, course, side)), Dump(doc.Analyze(course, side)));
                Assert.Equal((keys.Count - 1, 1), (doc.Reused, doc.Fresh));
            }
        }
    }

    [Fact]
    public void AnEditInOneBlockLeavesTheOthersToBeMoved()
    {
        string text = "BPM:120\nCOURSE:Easy\n#START\n1,\n#END\nCOURSE:Oni\nBALLOON:4\n#START\n7008,\n#SCROLL x\n1,\n#END\n";
        var doc = new TjaDocument(text);
        doc.Analyze("Easy"); doc.Analyze("Oni");
        var next = doc.WithChange(text.IndexOf("1,"), 0, "2", 1);
        var oni = next.Analyze("Oni");
        Assert.Equal((1, 0), (next.Reused, next.Fresh));
        // Moved by the one character: its spans and the line in its warning are where they now are.
        Assert.Equal(Dump(TjaParser.Analyze(next.Text, "Oni")), Dump(oni));
        Assert.Equal(next.Text.IndexOf("#SCROLL x"), oni.Diagnostics.Single(d => d.Kind == TjaDiagnosticKind.CommandDropped).Span.Start);
        next.Analyze("Easy");
        Assert.Equal((1, 1), (next.Reused, next.Fresh));
        // A header before both changes them both.
        var header = next.WithChange(0, 0, "OFFSET:1\n", 2);
        header.Analyze("Easy"); header.Analyze("Oni");
        Assert.Equal((0, 2), (header.Reused, header.Fresh));
    }

    // Everything an analysis says, as text.
    internal static string Dump(TjaAnalysis a)
    {
        var sb = new StringBuilder();
        sb.Append($"start {a.StartSpan} end {a.EndSpan}\n");
        foreach (var d in a.Diagnostics) sb.Append($"d {d.Code} {d.Line} {d.Span} {d.Message} [{string.Join(",", d.Args)}]\n");
        var c = a.Chart;
        if (c == null) return sb.Append("no chart\n").ToString();
        sb.Append($"chart {c.Title} {c.Subtitle} {c.Course} {c.Level} {c.Bpm} {c.Offset} {c.Duration} {ChartHash.Of(c)}\n");
        foreach (string w in c.Warnings) sb.Append("w ").Append(w).Append('\n');
        var bars = c.Bars.Select((b, i) => (b, i)).ToDictionary(x => x.b, x => x.i);
        foreach (var m in a.Measures) sb.Append($"m {m.Index} {m.Span} {m.StartTime} {m.EndTime} {m.Bpm} {m.Length} {m.Slots} {m.BranchId} {m.Route} {bars[m.Bar]}\n");
        string Span(object item) => a.SourceMap.TryGetSpan(item, out var s) ? s.ToString() : "-";
        foreach (var n in c.Notes)
        {
            a.SourceMap.TryGetTailSpan(n, out var tail);
            sb.Append($"n {(int)n.Kind} {Span(n)} {(n.IsLong ? tail.ToString() : "")} {a.SourceMap.NoteAt(a.SourceMap.TryGetSpan(n, out var s) ? s.Start : -1) == n}\n");
        }
        foreach (var b in c.Bars) sb.Append("b ").Append(Span(b)).Append('\n');
        foreach (object e in c.Tempos.Cast<object>().Concat(c.Gogos).Concat(c.Sections).Concat(c.JudgeMoves).Concat(c.LaneAlphas).Concat(c.Branches))
            sb.Append("e ").Append(e.GetType().Name).Append(' ').Append(Span(e)).Append('\n');
        return sb.ToString();
    }

    // TjaDocument moves a chart by copying it with other warnings; the copy must have every field.
    [Fact]
    public void AMovedChartKeepsEveryField()
    {
        var chart = TjaParser.Parse(Tja.Fixture("Donkama2000.tja"));
        var copy = chart.WithWarnings(new[] { "x" });
        foreach (var field in typeof(TaikoChart).GetFields())
        {
            object a = field.GetValue(chart), b = field.GetValue(copy);
            if (field.Name == nameof(TaikoChart.Warnings)) Assert.Equal(new[] { "x" }, (List<string>)b);
            else if (a is System.Collections.IEnumerable list && a is not string) Assert.Equal(list.Cast<object>(), ((System.Collections.IEnumerable)b).Cast<object>());
            else Assert.Equal(a, b);
        }
    }
}
