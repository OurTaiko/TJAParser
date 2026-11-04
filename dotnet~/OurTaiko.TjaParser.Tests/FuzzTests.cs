using OurTaiko;
using OurTaiko.Corpus;
using Xunit;

namespace OurTaiko.Tests;

// Randomly edited charts: whatever the text, reading it either works or fails with the
// exceptions the game and the servers catch (FormatException for a file or block that cannot be
// read, NotSupportedException for a note the parser does not know), never anything else.
public class FuzzTests
{
    const int Files = 3000;

    public static IEnumerable<object[]> Batches() => Enumerable.Range(0, 10).Select(b => new object[] { b });

    [Theory]
    [MemberData(nameof(Batches))]
    public void OnlyTheExpectedExceptions(int batch)
    {
        var failures = new List<string>();
        for (int i = batch; i < Files; i += 10)
        {
            string source = ChartCorpus.Generate(5000 + i % 150, 8, messy: i % 3 != 0);
            string text = ChartCorpus.Mutate(source, 7000 + i);
            Check(failures, i, () =>
            {
                var info = SongInfo.Read(text);
                foreach (var block in info.Blocks) Check(failures, i, () => TjaParser.Parse(text, block.Course, block.Side));
            });
            foreach (string course in SongInfo.CourseNames) Check(failures, i, () => TjaParser.Parse(text, course));
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Distinct().Take(10)));
    }

    static void Check(List<string> failures, int file, Action read)
    {
        try { read(); }
        catch (FormatException) { }
        catch (NotSupportedException) { }
        catch (Exception e) { failures.Add($"file {file}: {e.GetType().Name}: {e.Message}"); }
    }
}
