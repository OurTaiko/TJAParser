using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// NoteScroll and ScrollClockTimeline, moved from OurTaikoPlay with its tests (Finished/EditMode
// NoteScrollTests, EditMode ScrollModeTests T5–T7 and M2–M4, SuddenTests S2–S4): where notes are on
// the lane for normal, BM and HB scrolling and #SUDDEN, in whole milliseconds and pixels.
public class NoteScrollTests
{
    const double Distance = 1280 - 414, Travel = 1302;

    static TaikoChart Parse(string body, int bpm = 120)
        => TjaParser.Parse("TITLE:Scroll\nBPM:" + bpm + "\n\nCOURSE:Oni\n\n#START\n" + body + "\n#END");

    static TaikoChart Sudden(string body) => TjaParser.Parse("TITLE:Sudden\nBPM:120\nBALLOON:3\nCOURSE:Oni\n#START\n" + body + "\n#END");

    // Independent reference: OurTaikoPlayer's Player::get_position_x in ms, normal scroll mode.
    [Theory]
    [InlineData(120, 1, 1)]
    [InlineData(160, 1, 1)]
    [InlineData(160, 2, 1)]
    [InlineData(200, -1, 0.5)]
    [InlineData(160, 0, 1)]
    [InlineData(160, 0.5, -0.5)]
    public void MatchesTheOriginalMillisecondsFormula(double bpm, double scroll, double remaining)
    {
        double original = remaining * 1000 * (bpm / 240000 * scroll * (1280 - 414));
        Assert.Equal(original, NoteScroll.DistanceFromJudge(10, 10 - remaining, bpm, scroll, Distance), 6);
    }

    [Theory]
    [InlineData(120)]
    [InlineData(160)]
    [InlineData(240)]
    public void AFourBeatMeasureTravelsFromTheRightEdgeToTheJudge(double bpm)
    {
        Assert.Equal(866, NoteScroll.DistanceFromJudge(240 / bpm, 0, bpm, 1, Distance), 6);
        Assert.Equal(216.5, NoteScroll.DistanceFromJudge(60 / bpm, 0, bpm, 1, Distance), 6);
        Assert.Equal(0, NoteScroll.DistanceFromJudge(10, 10, bpm, 1, Distance));
    }

    [Fact]
    public void ARollsLengthIsItsHeadsSpeedOverItsTime()
    {
        Assert.Equal(649.5, NoteScroll.RollLength(TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n5008,\n#END").Notes[0], Distance), 6);
        // A BPM change inside it keeps the head's speed but moves the notes after it.
        var bpm = TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n50\n#BPMCHANGE 240\n08,\n1000,\n#END");
        Assert.Equal((1.25, 120.0, 240.0), (bpm.Notes[0].EndTime, bpm.Notes[0].Bpm, bpm.Notes[1].Bpm));
        Assert.Equal(541.25, NoteScroll.RollLength(bpm.Notes[0], Distance), 6);
        // So does a SCROLL change.
        var scroll = TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n#SCROLL 2\n50\n#SCROLL 1\n08,\n1000,\n#END");
        Assert.Equal((2.0, 1.0), (scroll.Notes[0].ScrollX, scroll.Notes[1].ScrollX));
        Assert.Equal(1299, NoteScroll.RollLength(scroll.Notes[0], Distance), 6);
    }

    [Fact]
    public void ALongerLaneKeepsTheTravelTime()
        => Assert.Equal(NoteScroll.DistanceFromJudge(2, 0.5, 160, 1, Distance) * 1.5, NoteScroll.DistanceFromJudge(2, 0.5, 160, 1, Distance * 1.5), 6);

    [Fact]
    public void TheClockRunsAtTheBpmAndThroughDelays()
    {
        // Measure 1 at 120 (0–2 s), measure 2 at 240 (2–3 s), a 1 s delay, the last note at 4 s.
        var chart = Parse("1,\n#BPMCHANGE 240\n1,\n#DELAY 1\n1,");
        var clocks = new ScrollClockTimeline(chart);
        double At(double t) => clocks.At(BranchRoute.Normal, t);
        Assert.Equal(-1, At(-2)); // before the start it runs back at the first BPM
        Assert.Equal(0.5, At(1));
        Assert.Equal(1.5, At(2.5));
        Assert.Equal(2.5, At(3.5)); // it keeps running through the delay
        Assert.Equal(chart.Notes[2].ScrollClock, At(4));
    }

    [Fact]
    public void EachRouteHasItsClockUntilTheBranchEnds()
    {
        // The branch is 2–4 s; 達人 slows to 60 BPM (and so is written last, its length decides).
        var chart = Parse("1,\n#BRANCHSTART p,0,0\n#N\n1,\n#E\n1,\n#M\n#BPMCHANGE 60\n1,\n#BRANCHEND\n1,");
        var clocks = new ScrollClockTimeline(chart);
        Assert.Equal(1.5, clocks.At(BranchRoute.Normal, 3));
        Assert.Equal(1.25, clocks.At(BranchRoute.Master, 3));
        // The branch ends at 6 s (達人's 4 s measure): every route goes on from 達人's clock 2.
        Assert.Equal(2, clocks.At(BranchRoute.Normal, 6));
        Assert.Equal(2.25, clocks.At(BranchRoute.Expert, 7));
        Assert.Equal(BranchRoute.Normal, ScrollClockTimeline.RouteOf(chart.Notes[0]));
    }

    [Fact]
    public void PositionsFollowTheModes()
    {
        var normal = Parse("#SCROLL 2\n1,\n#BPMCHANGE 240\n1,");
        var bm = Parse("#BMSCROLL\n#SCROLL 2+1i\n1,\n#BPMCHANGE 240\n1,\n1,");
        var hb = Parse("#HBSCROLL\n#SCROLL 2+1i\n1,\n#BPMCHANGE 240\n1,\n1,");
        (double, double) At(TaikoChart chart, int note, double t) => NoteScroll.Distance(chart.Notes[note], t, new ScrollClockTimeline(chart), Travel);
        // NM: the second note keeps its own 240 BPM and SCROLL 2 from afar.
        Assert.Equal((NoteScroll.DistanceFromJudge(2, 1, 240, 2, Travel), 0.0), At(normal, 1, 1));
        Assert.Equal(2604, At(normal, 1, 1).Item1);
        // BM: half a measure to go at 1 s, SCROLL ignored, no vertical part; HB: times SCROLL 2+1i.
        Assert.Equal((651.0, 0.0), At(bm, 1, 1));
        Assert.Equal((1302.0, 651.0), At(hb, 1, 1));
        // After the change to 240 every BM/HB note on the lane speeds up together.
        Assert.Equal(Travel, At(bm, 2, 2).Item1);
        Assert.Equal(System.Math.Truncate(0.75 * Travel), At(bm, 2, 2.25).Item1);
        Assert.Equal(System.Math.Truncate(1.125 * Travel), At(bm, 2, 1.75).Item1);
    }

    [Fact]
    public void RollsAreRigidInEveryMode()
    {
        Assert.Equal(1.75 * 2 * Travel, NoteScroll.RollLength(Parse("#HBSCROLL\n#SCROLL 2\n5000,\n#BPMCHANGE 60\n0008,").Notes[0], Travel));
        Assert.Equal(0.75 * Travel, NoteScroll.RollLength(Parse("#BMSCROLL\n#SCROLL 2\n5008,").Notes[0], Travel)); // BM ignores SCROLL
        Assert.Equal(NoteScroll.DistanceFromJudge(1.5, 0, 120, 2, Travel), NoteScroll.RollLength(Parse("#SCROLL 2\n5008,").Notes[0], Travel));
    }

    [Fact]
    public void TheClockComparesWholeMillisecondsAndRunsFromTheRawTime()
    {
        var chart = Parse("#BMSCROLL\n1,\n#BPMCHANGE 280\n1,", bpm: 140);
        var clocks = new ScrollClockTimeline(chart);
        // The change is at 1714.285… ms: at 1.713 s the 140 BPM point holds; at 1.714 s it counts
        // as reached, and the 280 point runs from its unrounded time.
        Assert.Equal(1713 * 140 / 15000.0, clocks.At16(BranchRoute.Normal, 1.713), 9);
        Assert.Equal(16 + (1714 - 15000 * 16 / 140.0) * 280 / 15000, clocks.At16(BranchRoute.Normal, 1.714), 9);
        Assert.Equal(clocks.At16(BranchRoute.Normal, 1.714), clocks.At16(BranchRoute.Normal, 1.7149));
    }

    [Fact]
    public void PositionsAreCutToWholePixels()
    {
        var chart = Parse("01005008,", bpm: 140);
        var clocks = new ScrollClockTimeline(chart);
        var don = chart.Notes[0];
        var roll = chart.Notes[1];
        // An eighth at 140 BPM: 214 ms → 214 × 140 / 240000 × 1302 = 162.5…
        Assert.Equal((162.0, 0.0), NoteScroll.Distance(don, 0, clocks, Travel));
        Assert.Equal(162, NoteScroll.Distance(don, 0.0004, clocks, Travel).X);
        Assert.Equal((0.857, 1.499), (roll.Time, roll.EndTime));
        Assert.Equal(System.Math.Truncate(857 * 140 / 240000.0 * Travel), NoteScroll.Distance(roll, 0, clocks, Travel).X);
        Assert.Equal(System.Math.Truncate(1499 * 140 / 240000.0 * Travel), NoteScroll.TailDistance(roll, 0, clocks, Travel).X);
        var hb = Parse("#HBSCROLL\n#SCROLL 1-0.5i\n01,");
        Assert.Equal((System.Math.Truncate(0.5 * Travel), System.Math.Truncate(-0.25 * Travel)), NoteScroll.Distance(hb.Notes[0], 0, new ScrollClockTimeline(hb), Travel));
    }

    // OpenTaiko's Game_Notes_Interval 960 on 1280 wide: 75% of the 1920 design width.
    [Fact]
    public void AMeasureIsThreeQuartersOfTheDesignWidth() => Assert.Equal(1440, NoteScroll.MeasureWidth);

    [Fact]
    public void SuddenNotesAppearAtTheirShowTime()
    {
        var chart = Sudden("0001,\n#SUDDEN 1 0.5\n1,");
        Assert.True(NoteScroll.Appeared(chart.Notes[0], -100));
        Assert.False(NoteScroll.Appeared(chart.Notes[1], 0.99));
        Assert.True(NoteScroll.Appeared(chart.Notes[1], 1.0));
        // Without a move time rolls and balloons stay hidden.
        Assert.Equal(new[] { false, false, true }, Sudden("#SUDDEN 1 0\n5008,\n7008,\n1,").Notes.Select(n => NoteScroll.Appeared(n, 100)));
    }

    [Fact]
    public void SuddenNotesStandWhereTheyWillStartUntilTheyMove()
    {
        // NM: the note at 1.5 s starts moving at 0.5 s, from (0.5 s × 120 / 240) × SCROLL 2 travels out.
        var normal = Sudden("#SUDDEN 2 1\n#SCROLL 2+1i\n0001,");
        var clocks = new ScrollClockTimeline(normal);
        var note = normal.Notes[0];
        Assert.Equal((1302.0, 976.0), NoteScroll.Distance(note, 0, clocks, Travel)); // vertical still moves
        Assert.Equal(1302, NoteScroll.Distance(note, 0.4, clocks, Travel).X);
        Assert.Equal(NoteScroll.RawDistance(note, 1, clocks, Travel), NoteScroll.Distance(note, 1, clocks, Travel));
        // BM: the note (clock 1.75, at 2.75 s) starts at 1.75 s, when the clock is 0.875.
        var bm = Sudden("#BMSCROLL\n#SUDDEN 2 1\n1,\n#BPMCHANGE 240\n0001,");
        var bmClocks = new ScrollClockTimeline(bm);
        Assert.Equal(System.Math.Truncate((1.75 - 0.875) * Travel), NoteScroll.Distance(bm.Notes[1], 1, bmClocks, Travel).X);
        Assert.Equal(System.Math.Truncate(0.25 * Travel), NoteScroll.Distance(bm.Notes[1], 2.5, bmClocks, Travel).X);
        // A roll stays rigid: only its head stands, the length is the same.
        var roll = Sudden("#SUDDEN 3 1\n5008,").Notes[0];
        Assert.Equal(NoteScroll.DistanceFromJudge(1.5, 0, 120, 1, Travel), NoteScroll.RollLength(roll, Travel));
        Assert.Equal(NoteScroll.DistanceFromJudge(0, -1, 120, 1, Travel), NoteScroll.Distance(roll, -2, clocks, Travel).X);
    }
}
