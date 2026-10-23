using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace OurTaiko
{
    // The canonical text of a parsed chart: what is played, in a fixed order and spelling, so two
    // files that play the same give the same text however their BPM, MEASURE and SCROLL are written.
    // A note is its kind, its times and how it moves (NoteScroll): in normal scrolling the speed
    // BPM × SCROLL, in BM/HB scrolling its beat clock and effective SCROLL together with the BPM
    // timeline the clock runs on. Left out: what only names or labels the chart (TITLE, SUBTITLE,
    // COURSE, LEVEL), OFFSET (the times are already relative to the audio), the written BPM and
    // beat positions (they drive beat animations and note text, not the notes), the parser's
    // warnings and the moji frames. ChartHash hashes this text. Any change to it, or to what
    // TjaParser puts in a TaikoChart, changes every hash: bump Version and recompute stored hashes.
    public static class ChartCanonical
    {
        public const int Version = 1;

        public static string Write(TaikoChart chart)
        {
            var notes = Index(chart.Notes);
            var bars = Index(chart.Bars);
            var sb = new StringBuilder();
            sb.Append("tjaparser-canonical ").Append(Version).Append('\n');
            Line(sb, "chart", N(chart.Duration));

            Section(sb, "notes", chart.Notes.Count);
            foreach (var note in chart.Notes) Note(sb, "n", note);
            Section(sb, "bars", chart.Bars.Count);
            foreach (var bar in chart.Bars) Note(sb, "b", bar);

            Section(sb, "branches", chart.Branches.Count);
            foreach (var br in chart.Branches)
                Line(sb, "br", I(br.Id), N(br.Time), N(br.EndTime), N(br.DecisionTime), I((int)br.Condition),
                    N(br.ExpertThreshold), N(br.MasterThreshold), B(br.BigOnly), B(br.Less),
                    B(br.HoldRoutes[0]), B(br.HoldRoutes[1]), B(br.HoldRoutes[2]),
                    I((int)br.Routes[0]), I((int)br.Routes[1]), I((int)br.Routes[2]),
                    Ref(br.FirstEntries[0], notes, bars), Ref(br.FirstEntries[1], notes, bars), Ref(br.FirstEntries[2], notes, bars));

            Section(sb, "sections", chart.Sections.Count);
            foreach (var s in chart.Sections) Line(sb, "s", N(s.Time), I(s.BranchId), I((int)s.Route));
            Section(sb, "judgemoves", chart.JudgeMoves.Count);
            foreach (var j in chart.JudgeMoves) Line(sb, "j", N(j.Time), N(j.Duration), N(j.MoveX), N(j.MoveY), N(j.FromX), N(j.FromY));
            Section(sb, "levelholds", chart.LevelHolds.Count);
            foreach (double t in chart.LevelHolds) Line(sb, "l", N(t));
            Section(sb, "gogos", chart.Gogos.Count);
            foreach (var g in chart.Gogos) Line(sb, "g", N(g.Time), B(g.On), I(g.BranchId), I((int)g.Route));

            // The beat clock's BPM timeline matters only where something scrolls by the beat clock.
            // Points at one time on one route collapse to the last; a point that keeps the BPM of the
            // previous one on its route changes nothing and goes.
            bool byClock = chart.Notes.Concat(chart.Bars).Any(n => n.ScrollMode != ScrollMode.Normal);
            var clocks = byClock ? Timeline(chart.ScrollClocks) : new List<ChartClockPoint>();
            Section(sb, "clocks", clocks.Count);
            foreach (var c in clocks) Line(sb, "c", N(c.Time), N(c.Clock), N(c.Bpm), I(c.BranchId), I((int)c.Route));

            // The played lists: the common part, then each route of each branch, as entries
            // referring to the notes and bars above (n = head, t = tail, b = bar).
            Section(sb, "notelists", chart.NoteLists.Count);
            foreach (var list in chart.NoteLists)
            {
                sb.Append("list ").Append(I(list.Count));
                foreach (var entry in list)
                    sb.Append(' ').Append(entry.IsBar ? "b" + I(bars[entry.Note]) : (entry.IsTail ? "t" : "n") + I(notes[entry.Note]));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        // A note or bar: kind, times, how it moves, and what it is on.
        static void Note(StringBuilder sb, string tag, ChartNote n)
        {
            string motion = n.ScrollMode == ScrollMode.Normal
                // Normal: speed across and down (NoteScroll.Place: ScrollX × BPM, ScrollY × BPM).
                ? "nm " + N(Speed(n.Bpm, n.ScrollX)) + " " + N(Speed(n.Bpm, n.ScrollY))
                // BM/HB: the beat clock at head and tail and the SCROLL it moves by (BM forces 1, 0).
                : (n.ScrollMode == ScrollMode.BMScroll ? "bm " : "hb ") + N(n.ScrollClock) + " " + N(n.EndScrollClock) + " "
                    + N(n.ScrollMode == ScrollMode.BMScroll ? 1 : n.ScrollX) + " " + N(n.ScrollMode == ScrollMode.BMScroll ? 0 : n.ScrollY);
            Line(sb, tag, I((int)n.Kind), N(n.Time), N(n.EndTime), motion,
                N(n.SuddenShow), N(n.SuddenMove), I(n.BalloonHits), I(n.BranchId), I((int)n.Route), B(n.Display), B(n.IsBranchStart));
        }

        // BPM × SCROLL, rounded so that 120 × 1 and 240 × 0.5 (or 150 × 0.8) read the same.
        static double Speed(double bpm, double scroll) => Math.Round(bpm * scroll, 9);

        static List<ChartClockPoint> Timeline(List<ChartClockPoint> points)
        {
            var kept = new List<ChartClockPoint>(points.Count);
            foreach (var point in points)
            {
                int last = kept.Count - 1;
                bool sameRoute = last >= 0 && kept[last].BranchId == point.BranchId && kept[last].Route == point.Route;
                if (sameRoute && kept[last].Time == point.Time) kept[last] = point;
                else if (sameRoute && kept[last].Bpm == point.Bpm) continue;
                else kept.Add(point);
            }
            return kept;
        }

        static Dictionary<ChartNote, int> Index(List<ChartNote> list)
        {
            var index = new Dictionary<ChartNote, int>(list.Count);
            for (int i = 0; i < list.Count; i++) index[list[i]] = i;
            return index;
        }

        static string Ref(ChartNote note, Dictionary<ChartNote, int> notes, Dictionary<ChartNote, int> bars)
            => note == null ? "-" : notes.TryGetValue(note, out int n) ? "n" + I(n) : bars.TryGetValue(note, out int b) ? "b" + I(b) : "?";

        static void Section(StringBuilder sb, string name, int count) => sb.Append(name).Append(' ').Append(I(count)).Append('\n');

        static void Line(StringBuilder sb, string tag, params string[] fields)
        {
            sb.Append(tag);
            foreach (string field in fields) sb.Append(' ').Append(field);
            sb.Append('\n');
        }

        // A number as the shortest decimal that reads back to the same double, culture-free;
        // negative zero as 0, infinities as inf / -inf.
        public static string N(double value)
        {
            if (double.IsNaN(value)) return "nan";
            if (double.IsPositiveInfinity(value)) return "inf";
            if (double.IsNegativeInfinity(value)) return "-inf";
            if (value == 0) return "0";
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
        static string B(bool value) => value ? "1" : "0";
    }
}
