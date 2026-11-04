using OurTaiko;
using OurTaiko.Corpus;
using Xunit;

namespace OurTaiko.Tests;

// I0-6 (Documentation~/Parser-TODO.md): what is in force at a position is what a note written
// there is read with. Checked against the notes themselves over the golden corpus.
public class PositionStateTests
{
    [Fact]
    public void AtANoteTheStateIsWhatTheNoteWasReadWith()
    {
        int checkedNotes = 0;
        var texts = GoldenTests.Corpus().Select(c => c.Text)
            .Concat(Enumerable.Range(0, 100).Select(i => ChartCorpus.Mutate(ChartCorpus.Generate(16000 + i % 30, 10, messy: true), 17000 + i)));
        foreach (string text in texts)
            foreach (string course in SongInfo.CourseNames)
            {
                var a = TjaParser.Analyze(text, course);
                if (a.Chart == null) continue;
                var notes = a.Chart.Notes;
                // A sample of each chart's notes: one state is one more read of the block.
                for (int i = 0; i < notes.Count; i += Math.Max(1, notes.Count / 12))
                {
                    var note = notes[i];
                    a.SourceMap.TryGetSpan(note, out var span);
                    var s = a.StateAt(span.Start);
                    Assert.NotNull(s);
                    Assert.Equal((note.Time, note.Beat, note.Bpm, note.ScrollX, note.ScrollY, note.ScrollMode, note.SuddenShow, note.SuddenMove),
                        (s.Time, s.Beat, s.Bpm, s.ScrollX, s.ScrollY, s.ScrollMode, s.SuddenShow, s.SuddenMove));
                    if (note.BranchId >= 0) Assert.Equal((note.BranchId, (BranchRoute?)note.Route), (s.BranchId, s.Route));
                    else Assert.Null(s.Route);
                    Assert.True(s.Slot < s.Slots);
                    checkedNotes++;
                }
            }
        Assert.True(checkedNotes > 5000, checkedNotes.ToString());
    }

    const string Text = "BPM:120\nCOURSE:Oni\nBALLOON:3,4\n#START\n1010,\n#GOGOSTART\n#SCROLL 2\n7008,\n"
        + "#BRANCHSTART p,1,2\n#N\n5000,\n#E\n1,\n#M\n1,\n#BRANCHEND\n#END\n";

    static TjaPositionState At(string what, int skip = 0)
    {
        var a = TjaParser.Analyze(Text);
        return a.StateAt(Text.IndexOf(what, StringComparison.Ordinal) + skip);
    }

    [Fact]
    public void TheStateHasGogoScrollRollsBalloonsAndSlots()
    {
        var balloon = At("7008");
        Assert.Equal((2.0, true, 2.0, 1, 0, 4, 3, 0, "BALLOON"), (balloon.Time, balloon.Gogo, balloon.ScrollX, balloon.Measure, balloon.Slot, balloon.Slots, balloon.NextBalloonHits, balloon.BalloonIndex, balloon.BalloonList));
        Assert.Null(balloon.OpenRoll);
        var end = At("7008", 3);
        Assert.Equal((NoteKind?)NoteKind.Balloon, end.OpenRoll);
        Assert.Equal((3, 4, 1), (end.Slot, end.NextBalloonHits, end.BalloonIndex));
        // At a comma every slot of the measure is read.
        var comma = At("1010,", 4);
        Assert.Equal((2.0, 4, 4), (comma.Time, comma.Slot, comma.Slots));
    }

    [Fact]
    public void TheStateKnowsTheBranchAndRoute()
    {
        var marker = At("#N");
        Assert.Equal((0, (BranchRoute?)null, 0), (marker.BranchId, marker.Route, marker.Slots));
        var roll = At("5000", 1);
        Assert.Equal((0, (BranchRoute?)BranchRoute.Normal, (NoteKind?)NoteKind.Roll), (roll.BranchId, roll.Route, roll.OpenRoll));
        var after = At("#BRANCHEND\n#END", 11);
        Assert.Equal((-1, (BranchRoute?)null), (after.BranchId, after.Route));
    }

    [Fact]
    public void OutsideTheBodyThereIsNoState()
    {
        var a = TjaParser.Analyze(Text);
        Assert.Null(a.StateAt(Text.IndexOf("BALLOON")));
        Assert.Null(a.StateAt(Text.IndexOf("#START") + 2));
        Assert.Null(a.StateAt(Text.IndexOf("#END\n") + 2));
        Assert.NotNull(a.StateAt(Text.IndexOf("#END\n")));
        Assert.Null(TjaParser.Analyze(Text, "Easy").StateAt(10));
    }
}
