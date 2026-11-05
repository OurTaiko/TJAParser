using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace OurTaiko
{
    // Tidying that changes nothing played: command names and header keys in capitals, and, when a
    // resolution is asked (slots per whole note, 16 for sixteenths), each measure written alone on
    // its line in that many slots of its length, where the notes allow it. Every block's hash is
    // checked: a block whose measures would play differently keeps them as written.
    public static class TjaFormat
    {
        public static List<TjaTextEdit> Edits(TjaDocument document, int resolution = 0, CancellationToken cancel = default)
        {
            string text = document.Text;
            var edits = new List<TjaTextEdit>();
            foreach (var token in document.Syntax)
            {
                if (token.Kind != TjaSyntaxKind.Command && token.Kind != TjaSyntaxKind.HeaderKey) continue;
                string written = text.Substring(token.Span.Start, token.Span.Length);
                string upper = written.ToUpperInvariant();
                if (upper != written) edits.Add(new TjaTextEdit(token.Span, upper));
            }
            if (resolution <= 0) return edits;

            // Measures, block by block, with the hashes they must keep.
            var keys = new HashSet<(string, string)>();
            var blocks = new List<(TjaDocumentBlock Block, string Hash, List<TjaTextEdit> Edits)>();
            foreach (var block in document.Blocks)
            {
                if (!keys.Add((block.Course, block.Side))) continue;
                var analysis = document.Analyze(block.Course, block.Side, cancel);
                if (analysis.Chart == null || analysis.HasErrors) continue;
                var measureEdits = new List<TjaTextEdit>();
                foreach (var m in analysis.Measures)
                    if (Rewrite(text, m, resolution) is string rewritten)
                        measureEdits.Add(new TjaTextEdit(TextSpan.FromBounds(m.Span.Start, m.Span.End - 1), rewritten));
                blocks.Add((block, ChartHash.Of(analysis.Chart), measureEdits));
            }
            // Keep a block's measures only where every block still plays as it did.
            var kept = new List<TjaTextEdit>(edits);
            foreach (var b in blocks) kept.AddRange(b.Edits);
            string after = TjaTextEdit.Apply(text, kept);
            foreach (var b in blocks)
            {
                if (b.Edits.Count == 0) continue;
                bool same;
                try { same = ChartHash.Of(TjaParser.Parse(after, b.Block.Course, b.Block.Side)) == b.Hash; }
                catch (FormatException) { same = false; }
                if (same) edits.AddRange(b.Edits);
            }
            return edits;
        }

        // A measure's notes in `resolution` slots per whole note, or null to leave it: only a
        // measure that is one line of notes and its comma (no commands, counts or comments in it).
        static string Rewrite(string text, ChartMeasure m, int resolution)
        {
            if (m.Slots == 0) return null;
            string written = text.Substring(m.Span.Start, m.Span.Length);
            if (written.Length != m.Slots + 1 || written[written.Length - 1] != ',') return null;
            for (int i = 0; i < m.Slots; i++) if ("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".IndexOf(written[i]) < 0) return null;
            double exact = resolution * m.Length;
            int target = (int)Math.Round(exact);
            if (target < 1 || Math.Abs(exact - target) > 1e-9 || target == m.Slots) return null;
            string notes = written.Substring(0, m.Slots);
            if (target % m.Slots == 0)
            {
                int k = target / m.Slots;
                var sb = new System.Text.StringBuilder(target);
                foreach (char c in notes) sb.Append(c).Append('0', k - 1);
                return sb.ToString();
            }
            if (m.Slots % target == 0)
            {
                int k = m.Slots / target;
                var sb = new System.Text.StringBuilder(target);
                for (int i = 0; i < m.Slots; i++)
                {
                    if (i % k == 0) sb.Append(notes[i]);
                    else if (notes[i] != '0') return null;
                }
                return sb.ToString();
            }
            return null;
        }
    }
}
