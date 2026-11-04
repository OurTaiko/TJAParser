using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace OurTaiko
{
    // An editor's checks of a document (TjaDiagnosticKind TJA3xxx), beyond what the parser reports:
    // what makes SongInfo refuse the file, LEVEL outside 1 to 10, a command spelt close to a known
    // one, a measure of unusual slots, a branch whose routes end apart. They are for the author;
    // the game and the servers do not see them.
    internal static class TjaLint
    {
        public static void Check(TjaDocument document, List<TjaDiagnostic> into, CancellationToken cancel)
        {
            Lines(document, into);
            var keys = new HashSet<(string, string)>();
            foreach (var block in document.Blocks)
            {
                if (!keys.Add((block.Course, block.Side))) continue;
                var analysis = document.Analyze(block.Course, block.Side, cancel);
                if (analysis.Chart != null) Block(analysis, into);
            }
        }

        static void Add(List<TjaDiagnostic> into, TjaDiagnosticKind kind, int line, TextSpan span, params object[] args)
            => into.Add(new TjaDiagnostic(kind, kind.Message(args), args, line, span));

        // The file as SongInfo reads it: its courses, #STARTs and the LEVEL in force at each.
        static void Lines(TjaDocument document, List<TjaDiagnostic> into)
        {
            var positions = new List<LineSource>();
            var seen = new HashSet<(Difficulty, string)>();
            string course = "Oni";
            TjaLine level = default;
            LineSource levelSource = default;
            bool hasLevel = false;
            int index = -1;
            foreach (var line in TjaLines.Read(document.Text, positions))
            {
                index++;
                var source = positions[index];
                if (line.Kind == TjaLineKind.Header && line.Key == "COURSE")
                {
                    course = line.Value;
                    hasLevel = false;
                    if (SongInfo.DifficultyOf(line.Value) == null)
                        Add(into, TjaDiagnosticKind.UnsupportedCourse, line.Line, Value(line, source), line.Value);
                }
                else if (line.Kind == TjaLineKind.Header && line.Key == "LEVEL")
                {
                    level = line; levelSource = source; hasLevel = true;
                }
                else if (line.Kind == TjaLineKind.Start)
                {
                    var span = source.Span(0, line.Text.Length);
                    if (line.Value != "" && line.Value != "P1" && line.Value != "P2")
                        Add(into, TjaDiagnosticKind.StartPlayer, line.Line, span, line.Value);
                    var difficulty = SongInfo.DifficultyOf(course);
                    string side = ChartSide.OfStart(line.Value);
                    if (difficulty != null && !seen.Add((difficulty.Value, side)))
                        Add(into, TjaDiagnosticKind.BlockWrittenTwice, line.Line, span, SongInfo.CourseName(difficulty.Value), side);
                    // LEVEL: missing, or a number outside 1 to 10 (one that does not read is the parser's).
                    if (!hasLevel) Add(into, TjaDiagnosticKind.LevelOutOfRange, line.Line, span, "missing");
                    else if (TjaNumber.TryParse(level.Value, out double stars) && ((int)stars < 1 || (int)stars > 10))
                        Add(into, TjaDiagnosticKind.LevelOutOfRange, level.Line, Value(level, levelSource), ((int)stars).ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        static TextSpan Value(TjaLine header, LineSource source)
        {
            string text = header.Text;
            int from = text.IndexOf(':') + 1;
            while (from < text.Length && char.IsWhiteSpace(text[from])) from++;
            return source.Span(from, text.Length);
        }

        static void Block(TjaAnalysis analysis, List<TjaDiagnostic> into)
        {
            foreach (var d in analysis.Diagnostics)
                if (d.Kind == TjaDiagnosticKind.UnknownCommand && Similar((string)d.Args[0]) is string name)
                    Add(into, TjaDiagnosticKind.SimilarCommand, d.Line, d.Span, name);
            var lines = analysis.Lines;
            // Measures with a hit note in them (by where the notes are written): a measure of blanks,
            // rolls and roll ends only places them, its slots say nothing of a rhythm.
            var heads = new HashSet<ChartMeasure>();
            var measures = analysis.Measures;
            foreach (var note in analysis.Chart.Notes)
            {
                if (note.IsLong || !analysis.SourceMap.TryGetSpan(note, out var at)) continue;
                int lo = 0, hi = measures.Count - 1, found = -1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (measures[mid].Span.Start <= at.Start) { found = mid; lo = mid + 1; }
                    else hi = mid - 1;
                }
                if (found >= 0 && measures[found].Span.Contains(at.Start)) heads.Add(measures[found]);
            }
            foreach (var m in measures)
            {
                // The note a slot is: slots per whole note (31 slots of #MEASURE 31/16 are sixteenths).
                double division = m.Slots / m.Length;
                if (m.Slots == 0 || !heads.Contains(m) || Usual(division)) continue;
                Add(into, TjaDiagnosticKind.UnusualSlots, lines.Line(m.Span.Start), m.Span,
                    m.Slots.ToString(CultureInfo.InvariantCulture), division.ToString("0.##", CultureInfo.InvariantCulture));
            }
            // Each branch: where each route it has written ends.
            var chart = analysis.Chart;
            var ends = new Dictionary<(int, BranchRoute), double>();
            foreach (var m in analysis.Measures)
                if (m.BranchId >= 0)
                    ends[(m.BranchId, m.Route)] = Math.Max(ends.TryGetValue((m.BranchId, m.Route), out double e) ? e : double.MinValue, m.EndTime);
            foreach (var branch in chart.Branches)
            {
                var written = new List<(BranchRoute Route, double End)>();
                foreach (BranchRoute route in new[] { BranchRoute.Normal, BranchRoute.Expert, BranchRoute.Master })
                    if (ends.TryGetValue((branch.Id, route), out double end)) written.Add((route, end));
                if (written.Count < 2) continue;
                bool apart = false;
                foreach (var w in written) apart |= Math.Abs(w.End - written[0].End) > 0.0005;
                if (!apart || !analysis.SourceMap.TryGetSpan(branch, out var span)) continue;
                var parts = new List<string>();
                foreach (var w in written)
                    parts.Add(w.Route.ToString().Substring(0, 1) + " " + (w.End - branch.Time).ToString("0.###", CultureInfo.InvariantCulture) + " s");
                Add(into, TjaDiagnosticKind.RoutesEndApart, lines.Line(span.Start), span, branch.Id, string.Join(", ", parts));
            }
        }

        // A whole number of slots per whole note that is 2^a·3^b: halves, thirds, quarters, sixths…
        static bool Usual(double division)
        {
            double whole = Math.Round(division);
            if (whole < 1 || Math.Abs(division - whole) > 1e-3 * division) return false;
            long n = (long)whole;
            while (n % 2 == 0) n /= 2;
            while (n % 3 == 0) n /= 3;
            return n == 1;
        }

        // The block command a name is a slip of (at most two edits, and less than half the name).
        static string Similar(string name)
        {
            string best = null;
            int bestDistance = int.MaxValue;
            foreach (var keyword in TjaSchema.Commands)
            {
                if ((keyword.Scope & TjaScope.Body) == 0 || keyword.Name == "#END") continue;
                int distance = Distance(name, keyword.Name);
                if (distance < bestDistance) { best = keyword.Name; bestDistance = distance; }
            }
            return bestDistance <= 2 && bestDistance * 2 < name.Length ? best : null;
        }

        static int Distance(string a, string b)
        {
            var row = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) row[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int diagonal = row[0];
                row[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int above = row[j];
                    row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                    diagonal = above;
                }
            }
            return row[b.Length];
        }
    }
}
