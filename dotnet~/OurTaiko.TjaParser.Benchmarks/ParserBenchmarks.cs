using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using OurTaiko;
using OurTaiko.Corpus;

// With no arguments every benchmark runs; BenchmarkDotNet options such as --filter narrow it.
BenchmarkSwitcher.FromAssembly(typeof(ParserBenchmarks).Assembly).Run(args.Length > 0 ? args : new[] { "--filter", "*" });

// Donkama2000: a real chart (four courses, 18 KB). Large: a generated chart with five courses of
// 400 measures each, with branches, rolls and every command (about 90 KB).
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ParserBenchmarks
{
    [Params("Donkama2000", "Large")]
    public string Chart;

    string text, course, side;

    [GlobalSetup]
    public void Setup()
    {
        text = Chart == "Large"
            ? ChartCorpus.Generate(7, 400, messy: false, courses: 5)
            : File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", Chart + ".tja"));
        // The last block, so Parse reads the whole file.
        var info = SongInfo.Read(text);
        var last = info.Blocks.OrderByDescending(b => b.Line).First();
        course = last.Course; side = last.Side;
    }

    [Benchmark] public SongInfo SongInfoRead() => SongInfo.Read(text);
    [Benchmark] public TaikoChart Parse() => TjaParser.Parse(text, course, side);
    [Benchmark] public List<ChartBlockHash> OfFile() => ChartHash.OfFile(text);
}
