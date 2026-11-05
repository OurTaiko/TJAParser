using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace OurTaiko
{
    // Rewrites of measures that keep every note where it is: a measure split in two at a slot, or
    // joined with the next. Each is checked on its result (the same notes at the same times, one bar
    // line more or less) and not offered otherwise.
    public static class TjaRefactor
    {
        const string NoteCharacters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        // The measure at an offset split before the slot there: `#MEASURE` lengths for both halves,
        // then the measure's own length again.
        public static TjaCodeAction Split(TjaDocument document, string course, string side, int offset, CancellationToken cancel = default)
        {
            var analysis = document.Analyze(course, side, cancel);
            var m = Plain(document, analysis, offset);
            if (m == null) return null;
            int k = offset - m.Span.Start;
            if (k < 1 || k >= m.Slots) return null;
            string notes = document.Text.Substring(m.Span.Start, m.Slots);
            string text = $"#MEASURE {Fraction(m.Length * k / m.Slots)}\n{notes.Substring(0, k)},\n#MEASURE {Fraction(m.Length * (m.Slots - k) / m.Slots)}\n{notes.Substring(k)},\n#MEASURE {Fraction(m.Length)}";
            return Checked(document, analysis, "Split the measure here", new TjaTextEdit(m.Span, text), +1);
        }

        // The measure at an offset joined with the next: both on one grid, one #MEASURE for the two,
        // then the second's length again.
        public static TjaCodeAction Join(TjaDocument document, string course, string side, int offset, CancellationToken cancel = default)
        {
            var analysis = document.Analyze(course, side, cancel);
            var m = Plain(document, analysis, offset);
            if (m == null) return null;
            var measures = analysis.Measures;
            int i = IndexOf(measures, m);
            if (i + 1 >= measures.Count) return null;
            var next = measures[i + 1];
            if (Plain(document, analysis, next.Span.Start) != next || next.BranchId != m.BranchId || next.Route != m.Route) return null;
            string between = document.Text.Substring(m.Span.End, next.Span.Start - m.Span.End);
            if (between.Trim().Length != 0) return null;
            // One grid for both: the greatest slot that divides both measures' slots (lengths as
            // fractions p/q, a slot of p/(q·slots)).
            if (!Parts(m.Length, out long p1, out long q1) || !Parts(next.Length, out long p2, out long q2)) return null;
            long a = p1 * q2 * next.Slots, b = p2 * q1 * m.Slots, g = Gcd(a, b);
            long k1 = a / g, k2 = b / g;
            if (k1 * m.Slots + k2 * next.Slots > 4096) return null;
            string first = Stretch(document.Text.Substring(m.Span.Start, m.Slots), (int)k1);
            string second = Stretch(document.Text.Substring(next.Span.Start, next.Slots), (int)k2);
            string text = $"#MEASURE {Fraction(m.Length + next.Length)}\n{first}{second},\n#MEASURE {Fraction(next.Length)}";
            return Checked(document, analysis, "Join with the next measure", new TjaTextEdit(TextSpan.FromBounds(m.Span.Start, next.Span.End), text), -1);
        }

        static int IndexOf(IReadOnlyList<ChartMeasure> measures, ChartMeasure m)
        {
            for (int i = 0; i < measures.Count; i++) if (measures[i] == m) return i;
            return -1;
        }

        // The measure at an offset if it is written alone on its line, notes and a comma only.
        static ChartMeasure Plain(TjaDocument document, TjaAnalysis analysis, int offset)
        {
            if (analysis.Chart == null || analysis.HasErrors) return null;
            string text = document.Text;
            foreach (var m in analysis.Measures)
            {
                if (!(m.Span.Start <= offset && offset < m.Span.End)) continue;
                if (m.Slots == 0 || m.Span.Length != m.Slots + 1 || text[m.Span.End - 1] != ',') return null;
                for (int i = 0; i < m.Slots; i++) if (NoteCharacters.IndexOf(text[m.Span.Start + i]) < 0) return null;
                int line = document.Lines.Line(m.Span.Start);
                int start = document.Lines.LineStart(line), end = line < document.Lines.LineCount ? document.Lines.LineStart(line + 1) : text.Length;
                if (text.Substring(start, end - start).Trim() != text.Substring(m.Span.Start, m.Span.Length)) return null;
                return m;
            }
            return null;
        }


        static long Gcd(long a, long b) => b == 0 ? a : Gcd(b, a % b);

        static string Stretch(string notes, int k)
        {
            var sb = new StringBuilder(notes.Length * k);
            foreach (char c in notes) sb.Append(c).Append('0', k - 1);
            return sb.ToString();
        }

        static readonly int[] Denominators = { 4, 8, 16, 32, 64, 128, 12, 24, 48, 96, 192, 3, 6, 5, 10, 20, 40, 7, 14, 28 };

        // A length in whole notes as a fraction p/q (0.75 as 3/4), if it is one of a usual q.
        static bool Parts(double length, out long num, out long den)
        {
            foreach (int d in Denominators)
            {
                double n = length * d;
                if (Math.Abs(n - Math.Round(n)) < 1e-9) { num = (long)Math.Round(n); den = d; return true; }
            }
            num = den = 0;
            return false;
        }

        // A length in whole notes as #MEASURE writes it.
        static string Fraction(double length)
            => Parts(length, out long num, out long den) ? num.ToString(CultureInfo.InvariantCulture) + "/" + den.ToString(CultureInfo.InvariantCulture)
                : length.ToString("R", CultureInfo.InvariantCulture) + "/1";

        // The edit if its result has the same notes at the same times and `bars` more bar lines.
        static TjaCodeAction Checked(TjaDocument document, TjaAnalysis analysis, string title, TjaTextEdit edit, int bars)
        {
            string after = TjaTextEdit.Apply(document.Text, new[] { edit });
            var result = TjaParser.Analyze(after, analysis.Course, analysis.Side);
            if (result.Chart == null || result.HasErrors || result.Chart.Bars.Count != analysis.Chart.Bars.Count + bars) return null;
            // Everything played but the bar lines the same (times, speeds, the beat clock BM/HB notes
            // move by: a floating sum over other measures may differ in its last digits).
            if (WithoutBars(result.Chart) != WithoutBars(analysis.Chart)) return null;
            return new TjaCodeAction { Title = title, Edits = new[] { edit } };
        }

        // The canonical text but bar lines and what refers to them (the lists, branches' first entries).
        internal static string WithoutBars(TaikoChart chart)
        {
            var sb = new StringBuilder();
            foreach (string line in ChartCanonical.Write(chart).Split('\n'))
                if (!(line.StartsWith("b ") || line.StartsWith("bars ") || line.StartsWith("list ") || line.StartsWith("br "))) sb.Append(line).Append('\n');
            return sb.ToString();
        }
    }
}
