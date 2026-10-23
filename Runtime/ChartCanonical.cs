using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OurTaiko
{
    // The canonical text of a parsed chart: everything TjaParser read that the game plays by, in a
    // fixed order and spelling, so two files that play the same give the same text. It leaves out
    // what only names or labels the chart (TITLE, SUBTITLE, COURSE, LEVEL), the parser's warnings
    // and the moji frames under the notes (NoteMoji), which are drawn from the notes themselves.
    // ChartHash hashes this text. Any change to it, or to what TjaParser puts in a TaikoChart,
    // changes every hash: bump Version and have the servers recompute their stored hashes.
    public static class ChartCanonical
    {
        public const int Version = 1;

        public static string Write(TaikoChart chart)
        {
            var notes = Index(chart.Notes);
            var bars = Index(chart.Bars);
            var sb = new StringBuilder();
            sb.Append("tjaparser-canonical ").Append(Version).Append('\n');
            Line(sb, "chart", N(chart.Bpm), N(chart.Offset), N(chart.Duration));

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
            Section(sb, "clocks", chart.ScrollClocks.Count);
            foreach (var c in chart.ScrollClocks) Line(sb, "c", N(c.Time), N(c.RawMs), N(c.Clock), N(c.Bpm), I(c.BranchId), I((int)c.Route));
            Section(sb, "judgemoves", chart.JudgeMoves.Count);
            foreach (var j in chart.JudgeMoves) Line(sb, "j", N(j.Time), N(j.Duration), N(j.MoveX), N(j.MoveY), N(j.FromX), N(j.FromY));
            Section(sb, "levelholds", chart.LevelHolds.Count);
            foreach (double t in chart.LevelHolds) Line(sb, "l", N(t));
            Section(sb, "gogos", chart.Gogos.Count);
            foreach (var g in chart.Gogos) Line(sb, "g", N(g.Time), B(g.On), I(g.BranchId), I((int)g.Route));
            Section(sb, "tempos", chart.Tempos.Count);
            foreach (var t in chart.Tempos) Line(sb, "t", N(t.Time), N(t.Bpm), N(t.Beat), N(t.ResumeTime), I(t.BranchId), I((int)t.Route));

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

        static void Note(StringBuilder sb, string tag, ChartNote n)
            => Line(sb, tag, I((int)n.Kind), N(n.Time), N(n.EndTime), N(n.Beat), N(n.Bpm), N(n.TailBpm),
                N(n.ScrollX), N(n.ScrollY), I((int)n.ScrollMode), N(n.ScrollClock), N(n.EndScrollClock),
                N(n.SuddenShow), N(n.SuddenMove), I(n.BalloonHits), I(n.BranchId), I((int)n.Route), B(n.Display), B(n.IsBranchStart));

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
