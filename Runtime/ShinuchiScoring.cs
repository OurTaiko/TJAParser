using System;
using System.Collections.Generic;
using System.Numerics;

namespace OurTaiko
{
    // The 真打 (shinuchi) scoring of a chart: what a 良, a 可 and a long-note hit are worth, and the
    // kiwami line. It depends on the chart alone (its course, notes, balloon counts and roll
    // durations), so every reader of the chart gets the same numbers. It is worked out from the notes
    // when asked and is not part of ChartCanonical: changing it changes no content hash.
    //
    //   c = 6 / 7.5 / 10 / 15 / 15 (Easy / Normal / Hard / Oni / Edit)
    //   k = floor(c × T), H = ceil(9k / 8), A = 100 × (B + H)
    //   良 = max(10, 10 × ceil((1000000 − A) / (10 × N))); 1000000 without judged notes
    //   可 = half the 良, down to ten points
    //   kiwami = max(1000000, N × 良 + A)
    //
    // N, B and T count the common notes and each branch's Master route (or the route drawn in its
    // place).
    public sealed class ShinuchiScoring
    {
        // Every hit of a drumroll, balloon or kusudama.
        public const int LongHitPoints = 100;
        // N: notes judged 良/可/不可 (1–4 and the Kadon).
        public int JudgeableNotes { get; }
        // Judgeable notes on each whole route (Normal, Expert, Master): OpenTaiko nノーツ数_Branch,
        // which sizes the soul gauge increments of that route.
        public IReadOnlyList<int> RouteNotes { get; }
        // B: the hits the balloons and kusudamas require, uncapped.
        public long BalloonHits { get; }
        // T: the drumrolls' ScoreDuration summed, in microseconds. A reversed roll (negative #DELAY)
        // adds nothing.
        public TjaFraction RollMicroseconds { get; }
        public int GoodPoints { get; }
        public int OkPoints { get; }
        // Rank thresholds are int; a budget past that saturates at int.MaxValue.
        public int KiwamiThreshold { get; }

        public static ShinuchiScoring Of(TaikoChart chart) => new ShinuchiScoring(chart);

        ShinuchiScoring(TaikoChart chart)
        {
            if (chart == null) throw new ArgumentNullException(nameof(chart));
            var difficulty = SongInfo.DifficultyOf(chart.Course)
                ?? throw new ArgumentException($"Unsupported course '{chart.Course}'.", nameof(chart));
            int judgeable = 0;
            long balloonHits = 0;
            TjaFraction rollMicroseconds = 0;
            foreach (var note in chart.Notes)
            {
                // A branch without #M counts the route drawn in its place.
                if (note.BranchId >= 0 && note.Route != chart.Branches[note.BranchId].ResolveRoute(BranchRoute.Master)) continue;
                if (IsJudgeable(note)) judgeable++;
                else if (note.IsBalloon) balloonHits += Math.Max(0, note.BalloonHits);
                else if (note.IsDrumroll && note.ScoreDuration > 0) rollMicroseconds += note.ScoreDuration;
            }
            var routeNotes = new int[3];
            foreach (var note in chart.Notes)
            {
                if (!IsJudgeable(note)) continue;
                for (int route = 0; route < 3; route++)
                    if (note.BranchId < 0 || note.Route == chart.Branches[note.BranchId].ResolveRoute((BranchRoute)route)) routeNotes[route]++;
            }
            JudgeableNotes = judgeable;
            RouteNotes = Array.AsReadOnly(routeNotes);
            BalloonHits = balloonHits;
            RollMicroseconds = rollMicroseconds;

            TjaFraction rate = difficulty switch
            {
                Difficulty.Easy => 6,
                Difficulty.Normal => new TjaFraction(15, 2),
                Difficulty.Hard => 10,
                _ => 15,
            };
            var k = (rollMicroseconds * rate / 1000000).Floor;
            var budget = (balloonHits + (9 * k + 7) / 8) * LongHitPoints;
            // With an exhausted budget a 良 is still worth ten points; the kiwami line keeps the budget.
            GoodPoints = judgeable == 0 ? 1000000
                : (int)BigInteger.Max(10, 10 * new TjaFraction(1000000 - budget, (BigInteger)10 * judgeable).Ceiling);
            OkPoints = GoodPoints / 2 / 10 * 10;
            KiwamiThreshold = (int)BigInteger.Min(int.MaxValue, BigInteger.Max(1000000, (BigInteger)GoodPoints * judgeable + budget));
        }

        // Notes 1–4 and the Kadon (G): judged 良/可/不可 and counted for score and gauge. An Ad-Lib (F)
        // is not: hit, it scores and fills the gauge on top, as a bonus. Nor is a mine (C): hit, it
        // only costs.
        public static bool IsJudgeable(ChartNote note) => note.Kind >= NoteKind.Don && note.Kind <= NoteKind.BigKa || note.IsKadon;
    }
}
