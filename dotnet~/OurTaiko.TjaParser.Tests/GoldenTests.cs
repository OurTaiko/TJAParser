using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using OurTaiko;
using OurTaiko.Corpus;
using Xunit;

namespace OurTaiko.Tests;

// The differential test: every file of a fixed corpus (the fixtures, generated charts and their
// mutations) is read as the game and the servers read it, and everything that comes out —
// SongInfo, every block's whole TaikoChart (also what the hash leaves out: RawMs, Beat, moji,
// tempos, lane alphas…), its hash, warnings, and the exception of a block or file that fails —
// is digested and compared with Fixtures/corpus.golden. An optimisation must leave every digest
// as it is. A change meant to alter behaviour regenerates the file and says so in its commit:
//
//   TJA_UPDATE_GOLDEN=1 dotnet test --filter GoldenTests
//
// For the servers' full chart libraries, tools/diff-corpus.sh compares two builds' `inspect`.
public class GoldenTests
{
    public static IEnumerable<(string Name, string Text)> Corpus()
    {
        yield return ("fixture/Donkama2000", Tja.Fixture("Donkama2000.tja"));
        yield return ("fixture/IrohaUta", Tja.Fixture("IrohaUta.tja"));
        yield return ("sample", Tja.Sample);
        yield return ("branching", Tja.Branching);
        for (int seed = 0; seed < 120; seed++)
            yield return ($"clean/{seed}", ChartCorpus.Generate(seed, 12 + seed % 20, messy: false));
        for (int seed = 0; seed < 120; seed++)
            yield return ($"messy/{seed}", ChartCorpus.Generate(1000 + seed, 12 + seed % 20, messy: true));
        for (int seed = 0; seed < 400; seed++)
        {
            string source = ChartCorpus.Generate(2000 + seed % 80, 10, messy: seed % 2 == 1);
            yield return ($"mutated/{seed}", ChartCorpus.Mutate(source, seed));
        }
    }

    [Fact]
    public void EveryCorpusFileReadsAsPinned()
    {
        var actual = Corpus().Select(c => c.Name + " " + Digest(Dump.Of(c.Text))).ToList();
        string path = GoldenPath();
        if (Environment.GetEnvironmentVariable("TJA_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(path, string.Join("\n", actual) + "\n");
            return;
        }
        var expected = File.ReadAllLines(path).Where(l => l.Length > 0).ToList();
        var changed = actual.Zip(expected).Where(p => p.First != p.Second).Select(p => p.First.Split(' ')[0]).ToList();
        Assert.True(expected.Count == actual.Count && changed.Count == 0,
            $"{changed.Count} of {actual.Count} corpus files read differently (corpus size {expected.Count} pinned): "
            + string.Join(", ", changed.Take(20)));
    }

    // OfFile reads the file's lines once for all its blocks; it must give what parsing each block
    // from the text gives (OfBlock, the path the digests above pin through TjaParser.Parse).
    [Fact]
    public void OfFileGivesWhatEachBlockGivesAlone()
    {
        foreach (var (name, text) in Corpus())
        {
            SongInfo info;
            try { info = SongInfo.Read(text); }
            catch (FormatException e)
            {
                Assert.Equal(e.Message, Assert.Throws<FormatException>(() => ChartHash.OfFile(text)).Message);
                continue;
            }
            var alone = info.Blocks.Select(b => Show(ChartHash.OfBlock(text, b))).ToList();
            Assert.True(alone.SequenceEqual(ChartHash.OfFile(text).Select(Show)), name);
            Assert.True(alone.SequenceEqual(ChartHash.OfFile(text, info).Select(Show)), name);
        }
    }

    static string Show(ChartBlockHash h)
        => $"{h.Difficulty} {h.Course} {h.Side} {h.Level} {h.IsBranching} {h.Hash} {h.Error} {string.Join("|", h.Warnings)} {h.Chart != null}";

    static string Digest(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant().Substring(0, 32);

    static string GoldenPath([CallerFilePath] string source = "")
        => Path.Combine(Path.GetDirectoryName(source), "Fixtures", "corpus.golden");
}

// Everything the library gives for a file, as text.
static class Dump
{
    public static string Of(string text)
    {
        var sb = new StringBuilder();
        SongInfo info;
        try { info = SongInfo.Read(text); }
        catch (Exception e)
        {
            sb.Append("songinfo ").Append(Error(e)).Append('\n');
            // A file SongInfo refuses can still be parsed block by block, as the game once did.
            foreach (string course in SongInfo.CourseNames)
                Block(sb, text, course, "single");
            return sb.ToString();
        }
        sb.Append("info ").Append(string.Join("|", info.Titles.OrderBy(t => t.Key).Select(t => t.Key + "=" + t.Value)))
            .Append(' ').Append(string.Join("|", info.Subtitles.OrderBy(t => t.Key).Select(t => t.Key + "=" + t.Value)))
            .Append(' ').Append(info.Genre).Append(' ').Append(info.Maker).Append(' ').Append(info.Wave).Append(' ').Append(info.Movie)
            .Append(' ').Append(N(info.Bpm)).Append(' ').Append(N(info.Offset)).Append(' ').Append(N(info.DemoStart))
            .Append(' ').Append(N(info.MovieOffset))
            .Append(' ').Append(string.Join("|", info.NotesDesigners.OrderBy(t => t.Key).Select(t => t.Key + "=" + t.Value))).Append('\n');
        foreach (var b in info.Blocks)
        {
            sb.Append("block ").Append(b.Line).Append(' ').Append(b.Course).Append(' ').Append((int)b.Difficulty).Append(' ')
                .Append(b.Side).Append(' ').Append(b.Player).Append(' ').Append(b.Style).Append(' ').Append(b.Level).Append(' ')
                .Append(b.IsBranching).Append(' ').Append(b.HasNotes).Append(' ').Append(b.Maker).Append('\n');
            Block(sb, text, b.Course, b.Side);
        }
        return sb.ToString();
    }

    static void Block(StringBuilder sb, string text, string course, string side)
    {
        TaikoChart chart;
        try { chart = TjaParser.Parse(text, course, side); }
        catch (Exception e)
        {
            sb.Append("parse ").Append(course).Append(' ').Append(side).Append(' ').Append(Error(e)).Append('\n');
            return;
        }
        sb.Append("parse ").Append(course).Append(' ').Append(side).Append(' ').Append(ChartHash.Of(chart)).Append('\n');
        Chart(sb, chart);
    }

    static string Error(Exception e) => e.GetType().Name + ": " + e.Message;

    static void Chart(StringBuilder sb, TaikoChart c)
    {
        sb.Append("chart ").Append(c.Title).Append(' ').Append(c.Subtitle).Append(' ').Append(c.Course).Append(' ').Append(c.Level)
            .Append(' ').Append(N(c.Bpm)).Append(' ').Append(N(c.Offset)).Append(' ').Append(N(c.Duration)).Append('\n');
        var index = new Dictionary<ChartNote, string>();
        for (int i = 0; i < c.Notes.Count; i++) index[c.Notes[i]] = "n" + i;
        for (int i = 0; i < c.Bars.Count; i++) index[c.Bars[i]] = "b" + i;
        foreach (var n in c.Notes) Note(sb, "n", n);
        foreach (var n in c.Bars) Note(sb, "b", n);
        foreach (var b in c.Branches)
            sb.Append("br ").Append(b.Id).Append(' ').Append(N(b.Time)).Append(' ').Append(N(b.EndTime)).Append(' ').Append(N(b.DecisionTime))
                .Append(' ').Append(b.Condition).Append(' ').Append(N(b.ExpertThreshold)).Append(' ').Append(N(b.MasterThreshold))
                .Append(' ').Append(b.BigOnly).Append(' ').Append(b.Less).Append(' ').Append(string.Join(",", b.HoldRoutes))
                .Append(' ').Append(string.Join(",", b.Routes)).Append(' ')
                .Append(string.Join(",", b.FirstEntries.Select(e => e == null ? "-" : index[e]))).Append('\n');
        foreach (var s in c.Sections) sb.Append("s ").Append(N(s.Time)).Append(' ').Append(s.BranchId).Append(' ').Append(s.Route).Append('\n');
        foreach (var p in c.ScrollClocks)
            sb.Append("c ").Append(N(p.Time)).Append(' ').Append(N(p.RawMs)).Append(' ').Append(N(p.Clock)).Append(' ').Append(N(p.Bpm))
                .Append(' ').Append(p.BranchId).Append(' ').Append(p.Route).Append('\n');
        foreach (var j in c.JudgeMoves)
            sb.Append("j ").Append(N(j.Time)).Append(' ').Append(N(j.Duration)).Append(' ').Append(N(j.MoveX)).Append(' ').Append(N(j.MoveY))
                .Append(' ').Append(N(j.FromX)).Append(' ').Append(N(j.FromY)).Append('\n');
        foreach (double t in c.LevelHolds) sb.Append("l ").Append(N(t)).Append('\n');
        foreach (var g in c.Gogos) sb.Append("g ").Append(N(g.Time)).Append(' ').Append(g.On).Append(' ').Append(g.BranchId).Append(' ').Append(g.Route).Append('\n');
        foreach (var t in c.Tempos)
            sb.Append("t ").Append(N(t.Time)).Append(' ').Append(N(t.Bpm)).Append(' ').Append(N(t.Beat)).Append(' ').Append(N(t.ResumeTime))
                .Append(' ').Append(t.BranchId).Append(' ').Append(t.Route).Append('\n');
        foreach (var a in c.LaneAlphas)
            sb.Append("a ").Append(N(a.Time)).Append(' ').Append(N(a.StartTime)).Append(' ').Append(N(a.EndTime)).Append(' ').Append(N(a.Alpha))
                .Append(' ').Append(a.BranchId).Append(' ').Append(a.Route).Append('\n');
        foreach (var list in c.NoteLists)
            sb.Append("list ").Append(string.Join(" ", list.Select(e => (e.IsTail ? "t" : "") + index[e.Note]))).Append('\n');
        foreach (string w in c.Warnings) sb.Append("w ").Append(w).Append('\n');
    }

    static void Note(StringBuilder sb, string tag, ChartNote n)
        => sb.Append(tag).Append(' ').Append((int)n.Kind).Append(' ').Append(N(n.Time)).Append(' ').Append(N(n.EndTime)).Append(' ')
            .Append(N(n.Beat)).Append(' ').Append(N(n.Bpm)).Append(' ').Append(N(n.ScrollX)).Append(' ').Append(N(n.ScrollY)).Append(' ')
            .Append(N(n.TailBpm)).Append(' ').Append(N(n.RawMs)).Append(' ').Append(n.ScrollMode).Append(' ').Append(N(n.ScrollClock)).Append(' ')
            .Append(N(n.EndScrollClock)).Append(' ').Append(N(n.SuddenShow)).Append(' ').Append(N(n.SuddenMove)).Append(' ')
            .Append(n.BalloonHits).Append(' ').Append(n.Moji).Append(' ').Append(n.BranchId).Append(' ').Append(n.Route).Append(' ')
            .Append(n.Display).Append(' ').Append(n.IsBranchStart).Append('\n');

    static string N(double v) => ChartCanonical.N(v);
}
