using OurTaiko;
using OurTaiko.Corpus;
using Xunit;

namespace OurTaiko.Tests;

// I1-4 (Documentation~/Parser-TODO.md): analyses are made in the background and read by many
// threads. A cancelled read leaves nothing half made; a snapshot reads the same from any thread
// and does not change when later versions are made.
public class ConcurrencyTests
{
    static readonly string Large = ChartCorpus.Generate(7, 200, messy: true, courses: 5);

    [Fact]
    public void ACancelledReadKeepsNothing()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => TjaParser.Analyze(Large, "Oni", "single", cancelled.Token));
        var document = new TjaDocument(Large);
        var (course, side) = document.Blocks.Select(b => (b.Course, b.Side)).Last();
        Assert.ThrowsAny<OperationCanceledException>(() => document.Analyze(course, side, cancelled.Token));
        var analysis = document.Analyze(course, side);
        Assert.Equal(DocumentTests.Dump(TjaParser.Analyze(Large, course, side)), DocumentTests.Dump(analysis));
        Assert.Equal(1, document.Fresh);
        Assert.ThrowsAny<OperationCanceledException>(() => analysis.StateAt(analysis.EndSpan.Start, cancelled.Token));
    }

    [Fact]
    public async Task CancellingAtAnyMomentEndsWhole()
    {
        var r = new Random(2);
        var (course, side) = new TjaDocument(Large).Blocks.Select(b => (b.Course, b.Side)).Last();
        string expected = DocumentTests.Dump(TjaParser.Analyze(Large, course, side));
        int cancelledReads = 0, finished = 0;
        for (int i = 0; i < 30; i++)
        {
            var document = new TjaDocument(Large, i);
            using var cancel = new CancellationTokenSource();
            var reading = Task.Run(() => document.Analyze(course, side, cancel.Token));
            await Task.Delay(r.Next(30));
            cancel.Cancel();
            try { await reading; finished++; }
            catch (OperationCanceledException) { cancelledReads++; }
            // Either way the document then has the whole analysis, never half of one.
            Assert.Equal(expected, DocumentTests.Dump(document.Analyze(course, side)));
        }
        Assert.Equal(30, cancelledReads + finished);
    }

    [Fact]
    public void ThreadsReadOneDocumentAtOnce()
    {
        var document = new TjaDocument(Large, 3);
        var keys = document.Blocks.Select(b => (b.Course, b.Side)).Distinct().ToList();
        var expected = keys.ToDictionary(k => k, k => DocumentTests.Dump(TjaParser.Analyze(Large, k.Course, k.Side)));
        var seen = new System.Collections.Concurrent.ConcurrentBag<((string, string) Key, TjaAnalysis Analysis)>();
        Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            var r = new Random(i);
            var key = keys[r.Next(keys.Count)];
            var a = document.Analyze(key.Course, key.Side);
            // Reading it: notes by offset, and a state.
            foreach (var note in a.Chart.Notes.Take(50))
                if (a.SourceMap.TryGetSpan(note, out var span)) Assert.Same(note, a.SourceMap.NoteAt(span.Start));
            if (i % 8 == 0) Assert.NotNull(a.StateAt(a.EndSpan.Start));
            seen.Add((key, a));
        });
        foreach (var group in seen.GroupBy(s => s.Key))
        {
            // One analysis per block, the same for every thread, and as a fresh one.
            Assert.Single(group.Select(g => g.Analysis).Distinct());
            Assert.Equal(expected[group.Key], DocumentTests.Dump(group.First().Analysis));
            Assert.Equal(3, group.First().Analysis.Version);
        }
    }

    [Fact]
    public void ASnapshotDoesNotChangeWithLaterVersions()
    {
        var first = new TjaDocument(Large, 1);
        var keys = first.Blocks.Select(b => (b.Course, b.Side)).Distinct().ToList();
        var before = keys.Select(k => DocumentTests.Dump(first.Analyze(k.Course, k.Side))).ToList();
        var next = first;
        for (int v = 2; v < 10; v++)
        {
            next = next.WithChange(next.Text.IndexOf("1,") + 1, 0, "2", v);
            foreach (var (course, side) in keys) Assert.Equal(v, next.Analyze(course, side).Version);
        }
        Assert.Equal(before, keys.Select(k => DocumentTests.Dump(first.Analyze(k.Course, k.Side))).ToList());
        Assert.All(keys, k => Assert.Equal(1, first.Analyze(k.Course, k.Side).Version));
    }
}
