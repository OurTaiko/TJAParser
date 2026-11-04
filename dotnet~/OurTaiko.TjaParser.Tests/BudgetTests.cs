using OurTaiko;
using OurTaiko.Corpus;
using Xunit;

namespace OurTaiko.Tests;

// I1-2 (Documentation~/Parser-TODO.md): what the editor may allocate per keystroke on the large
// generated chart (5 courses × 400 measures, about 90 KB), checked here; the times are tracked by
// EditorBenchmarks. Measured on one thread after a warm-up run.
public class BudgetTests
{
    const long MB = 1024 * 1024;

    static long Allocated(Action action)
    {
        action();
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void AKeystrokeStaysWithinItsAllocationBudget()
    {
        string text = ChartCorpus.Generate(7, 400, messy: false, courses: 5);
        var document = new TjaDocument(text);
        var keys = document.Blocks.Select(b => (b.Course, b.Side)).Distinct().ToList();
        foreach (var (c, s) in keys) document.Analyze(c, s);
        var (course, side) = keys[^1];
        var last = document.Analyze(course, side);
        int at = last.Measures[^1].Span.Start;
        last.SourceMap.TryGetSpan(last.Chart.Notes[^1], out var note);
        int version = 0;

        long analyze = Allocated(() => TjaParser.Analyze(text, course, side));
        long typeInBlock = Allocated(() => document.WithChange(at, 0, "1", ++version).Analyze(course, side));
        long typeAndReadAll = Allocated(() =>
        {
            var next = document.WithChange(at, 0, "1", ++version);
            foreach (var (c, s) in keys) next.Analyze(c, s);
        });
        long stateAt = Allocated(() => last.StateAt(note.Start));
        string measured = $"Analyze {analyze / 1024} KB, TypeInBlock {typeInBlock / 1024} KB, TypeAndReadAll {typeAndReadAll / 1024} KB, StateAt {stateAt / 1024} KB";
        Assert.True(analyze <= 4 * MB, measured);
        Assert.True(typeInBlock <= 6 * MB, measured);
        Assert.True(typeAndReadAll <= 13 * MB, measured);
        Assert.True(stateAt <= 3 * MB, measured);
    }
}
