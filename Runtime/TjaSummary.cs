using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace OurTaiko
{
    // A hint written into the text where it applies: a measure's number and time, and its BPM where
    // that changes.
    public readonly struct TjaInlayHint
    {
        public readonly int Offset;
        public readonly string Label;

        public TjaInlayHint(int offset, string label)
        {
            Offset = offset; Label = label;
        }

        public override string ToString() => Offset + " " + Label;
    }

    // One block at a glance, by route ("unbranched", or "normal", "expert", "master" as the CLI's
    // inspect names them): the notes to hit (don and ka, 1 to 4, as inspect counts them), the
    // combo (those and Kadon), the most of those in any one second, the length and the hash.
    public sealed class TjaBlockSummary
    {
        public TjaDocumentBlock Block { get; internal set; }
        public string Hash { get; internal set; }
        public double Duration { get; internal set; }
        public IReadOnlyDictionary<string, int> Notes { get; internal set; }
        public IReadOnlyDictionary<string, int> Combo { get; internal set; }
        public IReadOnlyDictionary<string, int> PeakPerSecond { get; internal set; }

        public string Title
        {
            get
            {
                string Routes(IReadOnlyDictionary<string, int> byRoute) => byRoute.Count == 1
                    ? First(byRoute).ToString(CultureInfo.InvariantCulture)
                    : string.Join("/", Values(byRoute));
                return $"{Routes(Notes)} notes · max combo {Routes(Combo)} · {Duration.ToString("0.###", CultureInfo.InvariantCulture)} s"
                    + $" · peak {Routes(PeakPerSecond)}/s · {Hash.Substring(0, Math.Min(Hash.Length, 15))}…";
            }
        }

        static int First(IReadOnlyDictionary<string, int> d)
        {
            foreach (var pair in d) return pair.Value;
            return 0;
        }

        static IEnumerable<string> Values(IReadOnlyDictionary<string, int> d)
        {
            foreach (var pair in d) yield return pair.Value.ToString(CultureInfo.InvariantCulture);
        }
    }

    public static class TjaSummary
    {
        // The hints of the measures that start in a stretch of the text.
        public static List<TjaInlayHint> Hints(TjaDocument document, TextSpan within, CancellationToken cancel = default)
        {
            var hints = new List<TjaInlayHint>();
            var keys = new HashSet<(string, string)>();
            foreach (var block in document.Blocks)
            {
                if (!keys.Add((block.Course, block.Side))) continue;
                if (block.EndSpan.End < within.Start || block.StartSpan.Start > within.End) continue;
                var analysis = document.Analyze(block.Course, block.Side, cancel);
                double bpm = double.NaN;
                foreach (var m in analysis.Measures)
                {
                    bool changed = m.Bpm != bpm;
                    bpm = m.Bpm;
                    if (m.Span.Start < within.Start || m.Span.Start > within.End) continue;
                    string label = "#" + (m.Index + 1).ToString(CultureInfo.InvariantCulture) + " " + m.StartTime.ToString("0.000", CultureInfo.InvariantCulture) + "s";
                    if (changed) label += " ♩" + m.Bpm.ToString("0.###", CultureInfo.InvariantCulture);
                    hints.Add(new TjaInlayHint(m.Span.Start, label));
                }
            }
            hints.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            return hints;
        }

        // Each block that parses, at its #START.
        public static List<TjaBlockSummary> Blocks(TjaDocument document, CancellationToken cancel = default)
        {
            var summaries = new List<TjaBlockSummary>();
            var keys = new HashSet<(string, string)>();
            foreach (var block in document.Blocks)
            {
                if (!keys.Add((block.Course, block.Side))) continue;
                var analysis = document.Analyze(block.Course, block.Side, cancel);
                if (analysis.Chart == null || analysis.HasErrors) continue;
                var chart = analysis.Chart;
                var routes = chart.Branches.Count == 0
                    ? new[] { ("unbranched", (BranchRoute?)null) }
                    : new[] { ("normal", (BranchRoute?)BranchRoute.Normal), ("expert", (BranchRoute?)BranchRoute.Expert), ("master", (BranchRoute?)BranchRoute.Master) };
                var notes = new Dictionary<string, int>();
                var combo = new Dictionary<string, int>();
                var peak = new Dictionary<string, int>();
                foreach (var (name, route) in routes)
                {
                    var times = new List<double>();
                    int hits = 0;
                    foreach (var note in chart.Notes)
                    {
                        if (route != null && note.BranchId >= 0 && note.Route != chart.Branches[note.BranchId].ResolveRoute(route.Value)) continue;
                        bool dk = note.Kind >= NoteKind.Don && note.Kind <= NoteKind.BigKa;
                        if (dk) hits++;
                        if (dk || note.Kind == NoteKind.Kadon) times.Add(note.Time);
                    }
                    times.Sort();
                    int most = 0;
                    for (int i = 0, j = 0; i < times.Count; i++)
                    {
                        while (times[i] - times[j] >= 1) j++;
                        most = Math.Max(most, i - j + 1);
                    }
                    notes[name] = hits; combo[name] = times.Count; peak[name] = most;
                }
                summaries.Add(new TjaBlockSummary
                {
                    Block = block, Hash = ChartHash.Of(chart), Duration = chart.Duration, Notes = notes, Combo = combo, PeakPerSecond = peak,
                });
            }
            return summaries;
        }
    }
}
