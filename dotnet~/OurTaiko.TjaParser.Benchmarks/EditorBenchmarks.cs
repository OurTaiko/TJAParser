using BenchmarkDotNet.Attributes;
using OurTaiko;
using OurTaiko.Corpus;

// The editor's work per keystroke (Documentation~/Parser-TODO.md, I1-2). A document with every
// block analysed takes one character in its last block's note line; then the edited block is
// read (TypeInBlock), or every block is asked for, the others moved (TypeAndReadAll). Analyze
// and StateAt are one fresh analysis of the last block and the state at its last note.
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class EditorBenchmarks
{
    [Params("Donkama2000", "Large")]
    public string Chart;

    TjaDocument document;
    TjaAnalysis last;
    List<(string Course, string Side)> keys;
    string course, side;
    int at, noteAt, version;

    [GlobalSetup]
    public void Setup()
    {
        string text = Chart == "Large"
            ? ChartCorpus.Generate(7, 400, messy: false, courses: 5)
            : File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", Chart + ".tja"));
        document = new TjaDocument(text);
        keys = document.Blocks.Select(b => (b.Course, b.Side)).Distinct().ToList();
        foreach (var (c, s) in keys) document.Analyze(c, s);
        (course, side) = keys[keys.Count - 1];
        last = document.Analyze(course, side);
        // A note line in the last block, and its last note.
        var lastBar = last.Measures[last.Measures.Count - 1];
        at = lastBar.Span.Start;
        last.SourceMap.TryGetSpan(last.Chart.Notes[last.Chart.Notes.Count - 1], out var span);
        noteAt = span.Start;
    }

    [Benchmark] public TjaAnalysis Analyze() => TjaParser.Analyze(document.Text, course, side);

    [Benchmark] public TjaAnalysis TypeInBlock() => document.WithChange(at, 0, "1", ++version).Analyze(course, side);

    [Benchmark]
    public TjaDocument TypeAndReadAll()
    {
        var next = document.WithChange(at, 0, "1", ++version);
        foreach (var (c, s) in keys) next.Analyze(c, s);
        return next;
    }

    [Benchmark] public TjaPositionState StateAt() => last.StateAt(noteAt);
}
