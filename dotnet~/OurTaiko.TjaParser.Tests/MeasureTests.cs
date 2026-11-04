using OurTaiko;
using OurTaiko.Corpus;
using Xunit;

namespace OurTaiko.Tests;

// I0-5 (Documentation~/Parser-TODO.md): an analysis lists the block's measures, one per bar line,
// and the measures each route plays.
public class MeasureTests
{
    static IEnumerable<string> Texts()
    {
        foreach (var (_, text) in GoldenTests.Corpus()) yield return text;
        for (int i = 0; i < 200; i++) yield return ChartCorpus.Mutate(ChartCorpus.Generate(14000 + i % 50, 10, messy: true), 15000 + i);
    }

    [Fact]
    public void MeasuresAreTheBarLinesAndEachRoutePlaysItsOwn()
    {
        int measures = 0;
        foreach (string text in Texts())
            foreach (string course in SongInfo.CourseNames)
            {
                var a = TjaParser.Analyze(text, course);
                if (a.Chart == null) continue;
                var chart = a.Chart;
                Assert.Equal(chart.Bars.Count, a.Measures.Count);
                Assert.Equal(chart.Bars.ToHashSet(), a.Measures.Select(m => m.Bar).ToHashSet());
                for (int i = 0; i < a.Measures.Count; i++)
                {
                    var m = a.Measures[i];
                    Assert.Equal(i, m.Index);
                    Assert.Equal(m.Bar.Time, m.StartTime);
                    Assert.True(a.SourceMap.TryGetSpan(m.Bar, out var span) && span == m.Span);
                    Assert.True(m.Length > 0 && m.Bpm > 0);
                    Assert.Equal((m.Bar.BranchId, m.Bar.Route), (m.BranchId, m.Route));
                }
                foreach (BranchRoute route in Enum.GetValues(typeof(BranchRoute)))
                {
                    // The bar lines in the note lists a player on this route goes through.
                    var lists = new List<List<ChartEntry>> { chart.NoteLists[0] };
                    for (int b = 0; b < chart.Branches.Count; b++) lists.Add(chart.NoteLists[1 + 3 * b + (int)chart.Branches[b].ResolveRoute(route)]);
                    var bars = lists.SelectMany(l => l).Where(e => e.IsBar).Select(e => e.Note).ToHashSet();
                    var played = a.Played(route);
                    Assert.Equal(bars, played.Select(m => m.Bar).ToHashSet());
                    for (int i = 1; i < played.Count; i++) Assert.True(played[i - 1].StartTime <= played[i].StartTime);
                }
                // A note is in a measure on its own route.
                foreach (var note in chart.Notes)
                {
                    a.SourceMap.TryGetSpan(note, out var span);
                    var m = a.Measures.Single(x => x.Span.Start <= span.Start && span.Start < x.Span.End);
                    Assert.Equal((note.BranchId, note.Route), (m.BranchId, m.Route));
                }
                measures += a.Measures.Count;
            }
        Assert.True(measures > 10000, measures.ToString());
    }

    [Fact]
    public void AMeasureKnowsItsTimesTempoLengthAndSlots()
    {
        string text = "BPM:120\nCOURSE:Oni\n#START\n1010,\n#MEASURE 3/4\n#BPMCHANGE 60\n100,\n,\n#END\n";
        var a = TjaParser.Analyze(text);
        Assert.Equal(new[] { (0.0, 2.0, 120.0, 1.0, 4), (2, 5, 60, 0.75, 3), (5, 8, 60, 0.75, 0) },
            a.Measures.Select(m => (m.StartTime, m.EndTime, m.Bpm, m.Length, m.Slots)));
        Assert.Equal(new[] { "1010,", "100,", "," }, a.Measures.Select(m => text.Substring(m.Span.Start, m.Span.Length)));
    }
}
