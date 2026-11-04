using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace OurTaiko
{
    // What a hover over a position shows: Markdown for the editor, and what it is about.
    public sealed class TjaHoverInfo
    {
        public TextSpan Span { get; internal set; }
        public string Markdown { get; internal set; }
        // A note (Tail: its end, an 8), with where it is in its measure.
        public ChartNote Note { get; internal set; }
        public bool Tail { get; internal set; }
        public ChartMeasure Measure { get; internal set; }
        public int Slot { get; internal set; }
        // A balloon's count: from a list (BalloonList's BalloonIndex-th) or written on it (Inline).
        public string BalloonList { get; internal set; }
        public int BalloonIndex { get; internal set; }
        public bool Inline { get; internal set; }
        // A command or header.
        public TjaKeyword Keyword { get; internal set; }
    }

    public static class TjaHover
    {
        // The frames of NoteMoji, by number.
        static readonly string[] Moji = { "ドン", "ド", "コ", "カッ", "カ", "ドン(大)", "カッ(大)", "連打ー", "連打(大)ー", "ふうせん", "ーっ!!", "くすだま", "カドン" };

        public static TjaHoverInfo At(TjaDocument document, int offset, CancellationToken cancel = default)
        {
            var block = document.BlockAt(offset);
            if (block != null && document.Lines.Line(offset) != block.Line)
            {
                var analysis = document.Analyze(block.Course, block.Side, cancel);
                if (analysis.Chart != null && analysis.SourceMap.NoteAt(offset, out bool tail) is ChartNote note)
                    return Note(document.Text, analysis, note, tail, offset, cancel);
            }
            return Keyword(document, offset);
        }

        static TjaHoverInfo Note(string text, TjaAnalysis analysis, ChartNote note, bool tail, int offset, CancellationToken cancel)
        {
            var map = analysis.SourceMap;
            map.TryGetSpan(note, out var head);
            var span = head;
            if (tail && map.TryGetTailSpan(note, out var end) && end.Contains(offset)) span = end;
            else if (tail) span = new TextSpan(offset, 1);
            var info = new TjaHoverInfo { Span = span, Note = note, Tail = tail };
            var sb = new StringBuilder();
            string name = Name(note.Kind);
            if (tail)
            {
                sb.Append("**End of the ").Append(name.ToLowerInvariant()).Append("**\n\n");
                sb.Append("From ").Append(Seconds(note.Time)).Append(" to ").Append(Seconds(note.EndTime))
                    .Append(" (").Append(Seconds(note.EndTime - note.Time)).Append(")\n");
                info.Markdown = sb.ToString();
                return info;
            }
            // Its measure and slot, from what the parser was reading there.
            foreach (var m in analysis.Measures)
                if (m.Span.Contains(head.Start)) { info.Measure = m; break; }
            var state = analysis.StateAt(head.Start, cancel);
            if (state != null) info.Slot = state.Slot;
            sb.Append("**").Append(name).Append("**");
            if (note.Moji >= 0 && note.Moji < Moji.Length) sb.Append(" ").Append(Moji[note.Moji]);
            sb.Append("\n\n");
            sb.Append("Time ").Append(Seconds(note.Time));
            if (note.IsLong) sb.Append(" to ").Append(Seconds(note.EndTime));
            sb.Append("  \n");
            if (info.Measure != null)
                sb.Append("Measure ").Append(info.Measure.Index + 1).Append(", slot ").Append(info.Slot + 1).Append(" of ").Append(info.Measure.Slots)
                    .Append(" (beat ").Append(Number(note.Beat)).Append(")  \n");
            sb.Append("Speed ").Append(Number(note.Bpm)).Append(" BPM × ").Append(Number(note.ScrollX));
            if (note.ScrollY != 0) sb.Append(" ").Append(note.ScrollY > 0 ? "+ " : "− ").Append(Number(Math.Abs(note.ScrollY))).Append("i");
            if (note.ScrollMode != ScrollMode.Normal) sb.Append(" (").Append(note.ScrollMode).Append(")");
            sb.Append("  \n");
            if (note.IsBalloon)
            {
                info.Inline = text.IndexOf('[', head.Start, head.Length) >= 0;
                sb.Append(note.BalloonHits).Append(" hits");
                if (info.Inline) sb.Append(", written on it");
                else if (state != null)
                {
                    info.BalloonList = state.BalloonList; info.BalloonIndex = state.BalloonIndex;
                    if (state.BalloonListed) sb.Append(", the ").Append(Ordinal(state.BalloonIndex + 1)).Append(" of ").Append(state.BalloonList);
                    else sb.Append(": ").Append(state.BalloonList).Append(" has no ").Append(Ordinal(state.BalloonIndex + 1)).Append(" count");
                }
                sb.Append("  \n");
            }
            if (note.BranchId >= 0) sb.Append("Branch ").Append(note.BranchId).Append(", ").Append(note.Route).Append(" route\n");
            info.Markdown = sb.ToString();
            return info;
        }

        // A command or header: what the schema says of it. On its argument or value too.
        static TjaHoverInfo Keyword(TjaDocument document, int offset)
        {
            var tokens = document.Syntax;
            int line = document.Lines.Line(offset);
            TjaSyntaxToken? at = null, keyword = null;
            foreach (var token in tokens)
            {
                if (token.Span.Start > offset) break;
                if (document.Lines.Line(token.Span.Start) != line) continue;
                if (token.Kind == TjaSyntaxKind.Command || token.Kind == TjaSyntaxKind.HeaderKey) keyword = token;
                if (token.Span.Contains(offset)) at = token;
            }
            if (at == null || keyword == null) return null;
            var kind = at.Value.Kind;
            if (kind != TjaSyntaxKind.Command && kind != TjaSyntaxKind.HeaderKey && kind != TjaSyntaxKind.Argument
                && kind != TjaSyntaxKind.HeaderValue && kind != TjaSyntaxKind.Colon) return null;
            var k = TjaSchema.Find(document.Text.Substring(keyword.Value.Span.Start, keyword.Value.Span.Length));
            if (k == null) return null;
            var sb = new StringBuilder();
            sb.Append("```tja\n").Append(Signature(k)).Append("\n```\n\n").Append(k.Summary).Append("\n\n");
            sb.Append(k.Origin == TjaOrigin.Tja ? "TJA" : k.Origin == TjaOrigin.OpenTaiko ? "OpenTaiko" : "OurTaiko (not in OpenTaiko)");
            sb.Append(k.InHash ? " · changes what is played" : " · does not change what is played");
            sb.Append(" · specification §").Append(k.Section);
            return new TjaHoverInfo { Span = at.Value.Span, Keyword = k, Markdown = sb.ToString() };
        }

        // `#NAME <a> [b]` or `NAME:<value>`.
        public static string Signature(TjaKeyword k)
        {
            var sb = new StringBuilder(k.Name);
            if (!k.IsCommand) sb.Append(':');
            // #BRANCHSTART's parameters are parted by commas, any other command's by spaces.
            string separator = k.Name == "#BRANCHSTART" ? ", " : " ";
            for (int i = 0; i < k.Parameters.Count; i++)
            {
                var p = k.Parameters[i];
                if (i > 0) sb.Append(separator);
                else if (k.IsCommand) sb.Append(' ');
                string name = p.Values.Count > 0 && p.Values.Count <= 4 ? string.Join("|", p.Values) : p.Name;
                sb.Append(p.Optional ? "[" + name + "]" : "<" + name + ">");
            }
            return sb.ToString();
        }

        static string Name(NoteKind kind) => kind switch
        {
            NoteKind.Don => "Don", NoteKind.Ka => "Ka", NoteKind.BigDon => "Big don", NoteKind.BigKa => "Big ka",
            NoteKind.Roll => "Drumroll", NoteKind.BigRoll => "Big drumroll", NoteKind.Balloon => "Balloon", NoteKind.Kusudama => "Kusudama",
            NoteKind.Mine => "Mine", NoteKind.AdLib => "Ad-Lib", NoteKind.Kadon => "Kadon", _ => kind.ToString(),
        };

        static string Seconds(double s) => s.ToString("0.###", CultureInfo.InvariantCulture) + " s";
        static string Number(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    }
}
