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
        // 2: times on a grid of a thousandth of a millisecond (TjaTime.OnGrid), no longer cut to a
        // whole one, and OFFSET to the same grid; places no longer added up slot by slot
        // (TjaParser's TimingState); beat clocks written to twelve decimals.
        public const int Version = 2;

        public static string Write(TaikoChart chart)
        {
            var notes = Index(chart.Notes);
            var bars = Index(chart.Bars);
            var w = new Writer(new StringBuilder());
            w.Sb.Append("tjaparser-canonical ").Append(Version).Append('\n');
            w.Tag("chart").N(chart.Duration).End();

            w.Section("notes", chart.Notes.Count);
            foreach (var note in chart.Notes) Note(w, "n", note);
            w.Section("bars", chart.Bars.Count);
            foreach (var bar in chart.Bars) Note(w, "b", bar);

            w.Section("branches", chart.Branches.Count);
            foreach (var br in chart.Branches)
            {
                // The Normal row's pair, which is every row's in a chart from TJA. Rows that differ
                // (fumen only) follow at the end of the line, so a TJA chart's line is as it was before them.
                w.Tag("br").I(br.Id).N(br.Time).N(br.EndTime).N(br.DecisionTime).I((int)br.Condition)
                    .N(br.ExpertThreshold(BranchRoute.Normal)).N(br.MasterThreshold(BranchRoute.Normal)).B(br.BigOnly).B(br.Less)
                    .B(br.HoldRoutes[0]).B(br.HoldRoutes[1]).B(br.HoldRoutes[2])
                    .I((int)br.Routes[0]).I((int)br.Routes[1]).I((int)br.Routes[2]);
                for (int r = 0; r < 3; r++) w.Ref(br.FirstEntries[r], notes, bars);
                if (!br.SameFromEveryRoute)
                    w.S("from").N(br.Thresholds[1, 0]).N(br.Thresholds[1, 1]).N(br.Thresholds[2, 0]).N(br.Thresholds[2, 1]);
                w.End();
            }

            w.Section("sections", chart.Sections.Count);
            foreach (var s in chart.Sections) w.Tag("s").N(s.Time).I(s.BranchId).I((int)s.Route).End();
            w.Section("judgemoves", chart.JudgeMoves.Count);
            foreach (var j in chart.JudgeMoves) w.Tag("j").N(j.Time).N(j.Duration).N(j.MoveX).N(j.MoveY).N(j.FromX).N(j.FromY).End();
            w.Section("levelholds", chart.LevelHolds.Count);
            foreach (double t in chart.LevelHolds) w.Tag("l").N(t).End();
            w.Section("gogos", chart.Gogos.Count);
            foreach (var g in chart.Gogos) w.Tag("g").N(g.Time).B(g.On).I(g.BranchId).I((int)g.Route).End();

            // The beat clock's BPM timeline matters only where something scrolls by the beat clock.
            // Points at one time on one route collapse to the last; a point that keeps the BPM of the
            // previous one on its route changes nothing and goes.
            bool byClock = chart.Notes.Concat(chart.Bars).Any(n => n.ScrollMode != ScrollMode.Normal);
            var clocks = byClock ? Timeline(chart.ScrollClocks) : new List<ChartClockPoint>();
            w.Section("clocks", clocks.Count);
            foreach (var c in clocks) w.Tag("c").N(c.Time).N(Clock(c.Clock)).N(c.Bpm).I(c.BranchId).I((int)c.Route).End();

            // The played lists: the common part, then each route of each branch, as entries
            // referring to the notes and bars above (n = head, t = tail, b = bar).
            w.Section("notelists", chart.NoteLists.Count);
            foreach (var list in chart.NoteLists)
            {
                w.Tag("list").I(list.Count);
                foreach (var entry in list)
                    w.Word(entry.IsBar ? "b" : entry.IsTail ? "t" : "n", entry.IsBar ? bars[entry.Note] : notes[entry.Note]);
                w.End();
            }
            return w.Sb.ToString();
        }

        // A note or bar: kind, times, how it moves, and what it is on.
        static void Note(Writer w, string tag, ChartNote n)
        {
            w.Tag(tag).I((int)n.Kind).N(n.Time).N(n.EndTime);
            // Normal: speed across and down (NoteScroll.Place: ScrollX × BPM, ScrollY × BPM).
            if (n.ScrollMode == ScrollMode.Normal) w.S("nm").N(Speed(n.Bpm, n.ScrollX)).N(Speed(n.Bpm, n.ScrollY));
            // BM/HB: the beat clock at head and tail and the SCROLL it moves by (BM forces 1, 0).
            else w.S(n.ScrollMode == ScrollMode.BMScroll ? "bm" : "hb").N(Clock(n.ScrollClock)).N(Clock(n.EndScrollClock))
                .N(n.ScrollMode == ScrollMode.BMScroll ? 1 : n.ScrollX).N(n.ScrollMode == ScrollMode.BMScroll ? 0 : n.ScrollY);
            w.N(n.SuddenShow).N(n.SuddenMove).I(n.BalloonHits).I(n.BranchId).I((int)n.Route).B(n.Display).B(n.IsBranchStart).End();
        }

        // BPM × SCROLL, rounded so that 120 × 1 and 240 × 0.5 (or 150 × 0.8) read the same.
        static double Speed(double bpm, double scroll) => Math.Round(bpm * scroll, 9);

        // A beat clock, in measures, to twelve decimals. TjaParser places a note by its measure's
        // start and its share of the measure, so the same rhythm on another grid has the same
        // clock to the last bit; this takes the last digits off where sums of other lengths meet
        // (a measure written as two, a #DELAY). Twelve, not fewer: a clock of the form k / 2ⁿ sits
        // half way between two numbers of n − 1 decimals, where noise would send it either way.
        static double Clock(double measures) => Math.Round(measures, 12);

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

        // Lines of space-separated fields, the numbers formatted straight into the builder (as N
        // and I spell them) rather than each into a string of its own.
        sealed class Writer
        {
            public readonly StringBuilder Sb;
            readonly char[] digits = new char[32];

            public Writer(StringBuilder sb) { Sb = sb; }

            public Writer Tag(string tag) { Sb.Append(tag); return this; }
            public void End() => Sb.Append('\n');
            public void Section(string name, int count) => Tag(name).I(count).End();
            public Writer S(string field) { Sb.Append(' ').Append(field); return this; }
            public Writer B(bool value) { Sb.Append(' ').Append(value ? '1' : '0'); return this; }

            public Writer I(int value)
            {
                Sb.Append(' ');
                Int(value);
                return this;
            }

            // A field of a letter and a number, as `n12`.
            public void Word(string letter, int value)
            {
                Sb.Append(' ').Append(letter);
                Int(value);
            }

            public void Ref(ChartNote note, Dictionary<ChartNote, int> notes, Dictionary<ChartNote, int> bars)
            {
                if (note == null) S("-");
                else if (notes.TryGetValue(note, out int n)) Word("n", n);
                else if (bars.TryGetValue(note, out int b)) Word("b", b);
                else S("?");
            }

            public Writer N(double value)
            {
                Sb.Append(' ');
                if (double.IsNaN(value)) Sb.Append("nan");
                else if (double.IsPositiveInfinity(value)) Sb.Append("inf");
                else if (double.IsNegativeInfinity(value)) Sb.Append("-inf");
                else if (value == 0) Sb.Append('0');
                else if (value.TryFormat(digits, out int written, "R", CultureInfo.InvariantCulture)) Sb.Append(digits, 0, written);
                else Sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
                return this;
            }

            void Int(int value)
            {
                if (value.TryFormat(digits, out int written, default, CultureInfo.InvariantCulture)) Sb.Append(digits, 0, written);
                else Sb.Append(value.ToString(CultureInfo.InvariantCulture));
            }
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
    }
}
