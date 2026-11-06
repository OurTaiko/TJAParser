using System;
using System.Collections.Generic;

namespace OurTaiko
{
    // Numbered by their TJA character: 1–9, and OpenTaiko's C (mine, a note not to hit), F (Ad-Lib,
    // a hidden bonus note) and G (Kadon, the purple note) read as base 36.
    public enum NoteKind { Don = 1, Ka, BigDon, BigKa, Roll, BigRoll, Balloon = 7, Kusudama = 9, Mine = 12, AdLib = 15, Kadon = 16 }
    public enum Judgment { None, Good, Ok, Bad, Roll }
    public enum BranchRoute { Normal, Expert, Master }
    // How a note scrolls (OpenTaiko #NMSCROLL / #BMSCROLL / #HBSCROLL): by its own time and BPM, or
    // by the beat clock shared with the notes around it, ignoring (BM) or applying (HB) its #SCROLL.
    public enum ScrollMode { Normal, BMScroll, HBScroll }
    // #BRANCHSTART conditions as OpenTaiko names them: p accuracy, pp 良 percentage, jp/jg/jb
    // 良/可/不可 counts (d is a big-note jp), r drumroll hits, rb balloon hits, s score.
    // None keeps the current route (an empty or unreadable condition).
    public enum BranchCondition { Accuracy, Drumroll, Score, PercentPerfect, JudgePerfect, JudgeGood, JudgeBad, BalloonHits, None }

    public sealed class ChartNote
    {
        public NoteKind Kind;
        public double Time, EndTime, Beat, Bpm, ScrollX = 1, ScrollY, TailBpm;
        // OpenTaiko's dbNowTime at the note: milliseconds from #START, not cut to whole
        // milliseconds as Time is (#DELAY included, OFFSET not). For analysis of note spacing
        // (the servers' difficulty constants); play and the content hash use Time.
        public double RawMs;
        public ScrollMode ScrollMode;
        // OpenTaiko's fBMSCROLLTime in measures of four beats: the beat clock at the note (and at a
        // long note's tail). Written measures advance it whatever the BPM; #DELAY at its BPM.
        public double ScrollClock, EndScrollClock;
        // OpenTaiko #SUDDEN, in seconds before the hit time: the note is drawn from SuddenShow
        // and starts to move at SuddenMove, standing where it will start until then. Infinite
        // when not set; bar lines never have them.
        public double SuddenShow = double.PositiveInfinity, SuddenMove = double.PositiveInfinity;
        // TJAP3's quirk: SUDDEN with an appearance but no move time hides rolls and balloons.
        public bool SuddenHidesRoll => !double.IsInfinity(SuddenShow) && double.IsPositiveInfinity(SuddenMove);
        public int BalloonHits;
        // Frame of notes/moji drawn under the note; assigned by NoteMoji.Assign.
        public int Moji;
        public int BranchId = -1;
        public BranchRoute Route;
        public bool Display = true, IsBranchStart;
        public bool IsLong => IsDrumroll || IsBalloon;
        public bool IsBalloon => Kind == NoteKind.Balloon || Kind == NoteKind.Kusudama;
        public bool IsKa => Kind == NoteKind.Ka || Kind == NoteKind.BigKa;
        // A 5/6 drumroll: a long note that is not a balloon.
        public bool IsDrumroll => Kind == NoteKind.Roll || Kind == NoteKind.BigRoll;
        // A big don or big ka (3/4).
        public bool IsBigNote => Kind == NoteKind.BigDon || Kind == NoteKind.BigKa;
        // OpenTaiko's Kadon (G): one don and one ka together, in either order.
        public bool IsKadon => Kind == NoteKind.Kadon;
        // OpenTaiko's Ad-Lib (F): hidden, hit with either colour, and only ever a bonus.
        public bool IsAdLib => Kind == NoteKind.AdLib;
        // OpenTaiko's mine (C): drawn, hit with either colour, and only ever a penalty.
        public bool IsMine => Kind == NoteKind.Mine;
    }

    // One object of an authored note list in source order. A long note appears
    // twice: its head, then its 8 tail (at EndTime, with the BPM in force there).
    public readonly struct ChartEntry
    {
        public readonly ChartNote Note;
        public readonly bool IsTail;
        public ChartEntry(ChartNote note, bool isTail) { Note = note; IsTail = isTail; }
        public bool IsBar => Note.Kind == 0;
        public double Time => IsTail ? Note.EndTime : Note.Time;
        public double Bpm => IsTail ? Note.TailBpm : Note.Bpm;
    }

    public sealed class ChartBranch
    {
        public int Id;
        // DecisionTime: when OpenTaiko judges the condition, at or before Time (see TjaParser).
        public double Time, EndTime, DecisionTime;
        public BranchCondition Condition;
        // The thresholds as fumen stores them, by the route being played when the condition is
        // judged: Thresholds[from, 0] reaches 玄人 and Thresholds[from, 1] 達人. Each row can differ
        // (fumen's NormalToMaster, AdvancedKeepAdvanced, MasterKeepMaster…), so a chart can send
        // the same count to different routes depending on where the player is. A TJA #BRANCHSTART
        // has one pair, which TjaParser writes into every row.
        public readonly double[,] Thresholds = new double[3, 2];
        public double ExpertThreshold(BranchRoute from) => Thresholds[(int)from, 0];
        public double MasterThreshold(BranchRoute from) => Thresholds[(int)from, 1];
        // Every route judged by the same pair, as in TJA.
        public bool SameFromEveryRoute
        {
            get
            {
                for (int r = 1; r < 3; r++)
                    if (!Same(Thresholds[r, 0], Thresholds[0, 0]) || !Same(Thresholds[r, 1], Thresholds[0, 1])) return false;
                return true;
            }
        }
        static bool Same(double a, double b) => a.Equals(b);
        public void SetThresholds(double expert, double master)
        {
            for (int r = 0; r < 3; r++) { Thresholds[r, 0] = expert; Thresholds[r, 1] = master; }
        }
        public void SetThresholds(BranchRoute from, double expert, double master)
        {
            Thresholds[(int)from, 0] = expert; Thresholds[(int)from, 1] = master;
        }
        // An upper-case condition (P, R, JP…) counts only big notes, big drumrolls or kusudama.
        public bool BigOnly;
        // A fourth argument `l`: the thresholds are upper bounds (value < master → 達人, else
        // value < expert → 玄人). OpenTaiko only; fumen always compares with ≥.
        public bool Less;
        // Routes with #LEVELHOLD: choosing one keeps it for every later branch (OpenTaiko). This
        // remembers that the player went through that route, which the thresholds cannot: they
        // only see the route being played now. fumen has no such hold.
        public readonly bool[] HoldRoutes = new bool[3];

        // The route the condition picks for `value` when `from` is being played. 達人 is tried
        // first, then 玄人 (OpenTaiko tBranchJudge); a negative threshold is reachable with no
        // notes played (`p,-1,101` always picks 玄人). Does not apply Routes, #LEVELHOLD or an
        // empty condition: the player decides those (they keep the current route).
        public BranchRoute Choose(double value, BranchRoute from)
        {
            double expert = ExpertThreshold(from), master = MasterThreshold(from);
            if (Less) return value < master ? BranchRoute.Master : value < expert ? BranchRoute.Expert : BranchRoute.Normal;
            return value >= master ? BranchRoute.Master : value >= expert ? BranchRoute.Expert : BranchRoute.Normal;
        }
        public readonly ChartNote[] FirstEntries = new ChartNote[3];
        // The route drawn for each requested one. An omitted #E uses #N and an omitted #M uses
        // #E, matching Fanmade's image renderer; a complete branch maps every route to itself.
        public readonly BranchRoute[] Routes = { BranchRoute.Normal, BranchRoute.Expert, BranchRoute.Master };
        public BranchRoute ResolveRoute(BranchRoute requested) => Routes[(int)requested];
        public bool HasAllRoutes => Routes[1] == BranchRoute.Expert && Routes[2] == BranchRoute.Master;
    }

    // From Time on a route's beat clock runs at Bpm from Clock. Common points (BranchId -1) are on
    // every route: the chart's start, each common #BPMCHANGE, and every branch's start and end.
    public sealed class ChartClockPoint
    {
        // Time is on whole milliseconds (compared as OpenTaiko does); RawMs is OpenTaiko's
        // unrounded bpm_change_time from #START, which the clock runs from.
        public double Time, RawMs, Clock, Bpm;
        public int BranchId = -1;
        public BranchRoute Route;
    }

    public sealed class ChartSection
    {
        public double Time;
        public int BranchId = -1;
        public BranchRoute Route;
    }

    // A #GOGOSTART / #GOGOEND at its own time, whether or not a note falls there.
    public sealed class ChartGogo
    {
        public double Time;
        public bool On;
        public int BranchId = -1;
        public BranchRoute Route;
    }

    // Beat/tempo anchor, including delays between notes or inside an empty measure.
    // Beat stays fixed from Time until ResumeTime; tempo changes resume immediately.
    public sealed class ChartTempo
    {
        public double Time, Bpm, Beat, ResumeTime;
        public int BranchId = -1;
        public BranchRoute Route;
    }

    public sealed class TaikoChart
    {
        public string Title = "Untitled", Subtitle = "", Course = "Oni";
        public int Level;
        public double Bpm = 120, Offset, Duration;
        public readonly List<ChartNote> Notes = new List<ChartNote>();
        public readonly List<ChartNote> Bars = new List<ChartNote>();
        public readonly List<ChartBranch> Branches = new List<ChartBranch>();
        public readonly List<ChartSection> Sections = new List<ChartSection>();
        // The beat clock's BPM points (OpenTaiko listBPM): ScrollClockTimeline reads them per route.
        public readonly List<ChartClockPoint> ScrollClocks = new List<ChartClockPoint>();
        // #JPOSSCROLL moves of the judge, in time order and linked (JudgeShift).
        public readonly List<ChartJudgeMove> JudgeMoves = new List<ChartJudgeMove>();
        // Times of #LEVELHOLD outside a route: from there the route being played is kept.
        public readonly List<double> LevelHolds = new List<double>();
        // Sorted by time; events at the same time keep their source order.
        public readonly List<ChartGogo> Gogos = new List<ChartGogo>();
        public readonly List<ChartTempo> Tempos = new List<ChartTempo>();
        // #LANE_ALPHA fades of the lane background, by StartTime (LaneAlpha.At reads them).
        public readonly List<ChartLaneAlpha> LaneAlphas = new List<ChartLaneAlpha>();
        // OurTaikoPlayer's NoteLists: the common part, then one per route of each branch,
        // each with its bar lines and long-note tails in place.
        public readonly List<List<ChartEntry>> NoteLists = new List<List<ChartEntry>>();
        public readonly List<string> Warnings = new List<string>();

        // The same chart (the same notes, bars and events) with other warnings: an analysis moved
        // to where its block now stands in an edited text (TjaDocument).
        internal TaikoChart WithWarnings(IEnumerable<string> warnings)
        {
            var copy = new TaikoChart { Title = Title, Subtitle = Subtitle, Course = Course, Level = Level, Bpm = Bpm, Offset = Offset, Duration = Duration };
            copy.Notes.AddRange(Notes); copy.Bars.AddRange(Bars); copy.Branches.AddRange(Branches); copy.Sections.AddRange(Sections);
            copy.ScrollClocks.AddRange(ScrollClocks); copy.JudgeMoves.AddRange(JudgeMoves); copy.LevelHolds.AddRange(LevelHolds);
            copy.Gogos.AddRange(Gogos); copy.Tempos.AddRange(Tempos); copy.LaneAlphas.AddRange(LaneAlphas); copy.NoteLists.AddRange(NoteLists);
            copy.Warnings.AddRange(warnings);
            return copy;
        }
    }
}
