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

        // Markdown in a language ("zh…", "ja…"; anything else, or null, is English).
        public static TjaHoverInfo At(TjaDocument document, int offset, CancellationToken cancel = default, string language = null)
        {
            var block = document.BlockAt(offset);
            if (block != null && document.Lines.Line(offset) != block.Line)
            {
                var analysis = document.Analyze(block.Course, block.Side, cancel);
                if (analysis.Chart != null && analysis.SourceMap.NoteAt(offset, out bool tail) is ChartNote note)
                    return Note(document.Text, analysis, note, tail, offset, cancel, language);
            }
            return Keyword(document, offset, language);
        }

        static TjaHoverInfo Note(string text, TjaAnalysis analysis, ChartNote note, bool tail, int offset, CancellationToken cancel, string language)
        {
            var map = analysis.SourceMap;
            map.TryGetSpan(note, out var head);
            var span = head;
            if (tail && map.TryGetTailSpan(note, out var end) && end.Contains(offset)) span = end;
            else if (tail) span = new TextSpan(offset, 1);
            var info = new TjaHoverInfo { Span = span, Note = note, Tail = tail };
            var sb = new StringBuilder();
            string name = TjaText.NoteName(note.Kind, language);
            if (tail)
            {
                sb.Append(TjaText.Format(language, "**End of the {0}**", "**{0}的结尾**", "**{0}の終わり**", TjaText.Of(language) == TjaText.Lang.En ? name.ToLowerInvariant() : name)).Append("\n\n");
                sb.Append(TjaText.Format(language, "From {0} to {1} ({2})", "从 {0} 到 {1}（{2}）", "{0} から {1}（{2}）",
                    Seconds(note.Time, language), Seconds(note.EndTime, language), Seconds(note.EndTime - note.Time, language))).Append("\n");
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
            sb.Append(TjaText.Format(language, "Time {0}", "时刻 {0}", "時刻 {0}", Seconds(note.Time, language)));
            if (note.IsLong) sb.Append(TjaText.Format(language, " to {0}", " 到 {0}", " 〜 {0}", Seconds(note.EndTime, language)));
            sb.Append("  \n");
            if (info.Measure != null)
                sb.Append(TjaText.Format(language, "Measure {0}, slot {1} of {2} (beat {3})", "第 {0} 小节，{2} 格中的第 {1} 格（第 {3} 拍）", "{0} 小節目、{2} 個中 {1} 個目（{3} 拍）",
                    info.Measure.Index + 1, info.Slot + 1, info.Measure.Slots, Number(note.Beat))).Append("  \n");
            sb.Append(TjaText.Pick(language, "Speed ", "速度 ", "速さ ")).Append(Number(note.Bpm)).Append(" BPM × ").Append(Number(note.ScrollX));
            if (note.ScrollY != 0) sb.Append(" ").Append(note.ScrollY > 0 ? "+ " : "− ").Append(Number(Math.Abs(note.ScrollY))).Append("i");
            if (note.ScrollMode != ScrollMode.Normal) sb.Append(" (").Append(note.ScrollMode).Append(")");
            sb.Append("  \n");
            if (note.IsBalloon)
            {
                info.Inline = text.IndexOf('[', head.Start, head.Length) >= 0;
                sb.Append(TjaText.Format(language, "{0} hits", "{0} 打", "{0} 打", note.BalloonHits));
                if (info.Inline) sb.Append(TjaText.Pick(language, ", written on it", "，写在音符上", "、音符に書かれている"));
                else if (state != null)
                {
                    info.BalloonList = state.BalloonList; info.BalloonIndex = state.BalloonIndex;
                    int n = state.BalloonIndex + 1;
                    if (state.BalloonListed) sb.Append(TjaText.Format(language, ", the {0} of {1}", "，{1} 的第 {2} 个", "、{1} の {2} 番目", TjaText.Ordinal(n), state.BalloonList, n));
                    else sb.Append(TjaText.Format(language, ": {1} has no {0} count", "：{1} 没有第 {2} 个打数", "：{1} に {2} 番目の打数がない", TjaText.Ordinal(n), state.BalloonList, n));
                }
                sb.Append("  \n");
            }
            if (note.BranchId >= 0)
                sb.Append(TjaText.Format(language, "Branch {0}, {1} route", "分歧 {0}，{1}路线", "分岐 {0}、{1}ルート", note.BranchId, TjaText.RouteName(note.Route, language))).Append("\n");
            info.Markdown = sb.ToString();
            return info;
        }

        // A command or header: what the schema says of it. On its argument or value too.
        static TjaHoverInfo Keyword(TjaDocument document, int offset, string language)
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
            sb.Append("```tja\n").Append(Signature(k)).Append("\n```\n\n").Append(k.SummaryIn(language)).Append("\n\n");
            sb.Append(k.Origin == TjaOrigin.Tja ? "TJA" : k.Origin == TjaOrigin.OpenTaiko ? "OpenTaiko"
                : TjaText.Pick(language, "OurTaiko (not in OpenTaiko)", "OurTaiko（OpenTaiko 中没有）", "OurTaiko（OpenTaiko にはない）"));
            sb.Append(k.InHash ? TjaText.Pick(language, " · changes what is played", " · 会改变游玩内容", " · 演奏内容が変わる")
                : TjaText.Pick(language, " · does not change what is played", " · 不改变游玩内容", " · 演奏内容は変わらない"));
            sb.Append(TjaText.Pick(language, " · specification §", " · 规范 §", " · 仕様 §")).Append(k.Section);
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


        static string Seconds(double s, string language) => s.ToString("0.###", CultureInfo.InvariantCulture) + TjaText.Pick(language, " s", " 秒", " 秒");
        static string Number(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
