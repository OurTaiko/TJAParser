using OurTaiko;
using OurTaiko.Corpus;
using Xunit;

namespace OurTaiko.Tests;

// I3-3 (Documentation~/Parser-TODO.md): splitting and joining measures keeps every note where it
// is. In the canonical text only bar lines and what refers to them may change.
public class RefactorTests
{
    // The canonical text without what bar lines are part of.
    static string WithoutBars(TaikoChart chart) => string.Join("\n", ChartCanonical.Write(chart).Split('\n')
        .Where(l => !(l.StartsWith("b ") || l.StartsWith("bars ") || l.StartsWith("list ") || l.StartsWith("br "))));

    [Fact]
    public void SplittingAndJoiningKeepTheNotes()
    {
        int done = 0;
        foreach (int seed in Enumerable.Range(0, 30))
        {
            string text = ChartCorpus.Generate(22000 + seed, 10, messy: false);
            var document = new TjaDocument(text);
            foreach (var block in document.Blocks)
            {
                var analysis = document.Analyze(block.Course, block.Side);
                if (analysis.Chart == null) continue;
                foreach (var m in analysis.Measures.Where(m => m.Slots >= 2))
                {
                    foreach (var action in new[] { TjaRefactor.Split(document, block.Course, block.Side, m.Span.Start + m.Slots / 2), TjaRefactor.Join(document, block.Course, block.Side, m.Span.Start) })
                    {
                        if (action == null) continue;
                        string after = TjaTextEdit.Apply(text, action.Edits);
                        var chart = TjaParser.Parse(after, block.Course, block.Side);
                        Assert.Equal(WithoutBars(analysis.Chart), WithoutBars(chart));
                        done++;
                    }
                    break;
                }
            }
        }
        Assert.True(done > 20, done.ToString());
    }

    [Fact]
    public void ASplitAndAJoinAsWritten()
    {
        string text = "BPM:120\nCOURSE:Oni\n#START\n#MEASURE 3/4\n102010,\n1,\n#END\n";
        var document = new TjaDocument(text);
        var split = TjaRefactor.Split(document, "Oni", "single", text.IndexOf("102010") + 2);
        Assert.Equal("BPM:120\nCOURSE:Oni\n#START\n#MEASURE 3/4\n#MEASURE 1/4\n10,\n#MEASURE 2/4\n2010,\n#MEASURE 3/4\n1,\n#END\n", TjaTextEdit.Apply(text, split.Edits));
        // Eighths and a dotted half on one grid of eighths.
        var join = TjaRefactor.Join(document, "Oni", "single", text.IndexOf("102010"));
        Assert.Equal("BPM:120\nCOURSE:Oni\n#START\n#MEASURE 3/4\n#MEASURE 6/4\n102010100000,\n#MEASURE 3/4\n#END\n", TjaTextEdit.Apply(text, join.Edits));
        // Not at a measure's first slot, nor in a measure with a command in it.
        Assert.Null(TjaRefactor.Split(document, "Oni", "single", text.IndexOf("102010")));
        Assert.Null(TjaRefactor.Split(new TjaDocument("COURSE:Oni\n#START\n10\n#SCROLL 2\n20,\n#END\n"), "Oni", "single", 21));
    }
}
