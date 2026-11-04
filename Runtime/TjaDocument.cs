using System;
using System.Collections.Generic;
using System.Threading;

namespace OurTaiko
{
    // One #START of a document: COURSE as written (what Analyze takes) and its side.
    public sealed class TjaDocumentBlock
    {
        public string Course { get; internal set; }
        public string Side { get; internal set; }
        public int Line { get; internal set; }
        public TextSpan StartSpan { get; internal set; }
    }

    // An editor's text at one version, read block by block as asked (Analyze). A new version
    // (WithText, WithChange) reuses the analyses of the one before for every block the edit did
    // not touch: its lines and every header line before it are the same, only moved. Those
    // analyses are moved to where the block now stands rather than read again, and are equal to
    // a fresh TjaParser.Analyze. A snapshot is not changed by later versions.
    public sealed class TjaDocument
    {
        public string Text { get; }
        public int Version { get; }

        readonly object gate = new object();
        // This version's analyses, by (course as asked, side).
        readonly Dictionary<(string, string), TjaAnalysis> analyses = new Dictionary<(string, string), TjaAnalysis>();
        // Analyses of earlier versions not yet asked for here: reused if still valid, else read again.
        readonly Dictionary<(string, string), TjaAnalysis> earlier;
        List<TjaDocumentBlock> blocks;
        // The text's lines and line starts, read once for every block moved into this version.
        List<TjaLine> lines;
        TjaSourceText lineStarts;

        // Made once, whichever thread asks first (another may make one too; one is kept).
        // Lines and columns of Text.
        public TjaSourceText Lines => LineStarts;

        List<TjaLine> AllLines => LazyInitializer.EnsureInitialized(ref lines, () => new List<TjaLine>(TjaLines.Read(Text)));
        TjaSourceText LineStarts => LazyInitializer.EnsureInitialized(ref lineStarts, () => new TjaSourceText(Text));

        // How many analyses this version moved from an earlier one, and read afresh.
        internal int Reused, Fresh;

        public TjaDocument(string text, int version = 0) : this(text, version, null) { }

        TjaDocument(string text, int version, Dictionary<(string, string), TjaAnalysis> earlier)
        {
            Text = text ?? "";
            Version = version;
            this.earlier = earlier ?? new Dictionary<(string, string), TjaAnalysis>();
        }

        // The next version with this text. This one stays as it is, for whoever still reads it.
        public TjaDocument WithText(string text, int version)
        {
            Dictionary<(string, string), TjaAnalysis> carried;
            lock (gate)
            {
                carried = new Dictionary<(string, string), TjaAnalysis>(earlier);
                foreach (var pair in analyses) carried[pair.Key] = pair.Value;
            }
            return new TjaDocument(text, version, carried);
        }

        // The next version with `length` characters from `start` replaced by `text` (an LSP change).
        public TjaDocument WithChange(int start, int length, string text, int version)
            => WithText(Text.Substring(0, start) + text + Text.Substring(start + length), version);

        List<TjaSyntaxToken> syntax;

        // The whole text as syntax tokens (TjaSyntax.Tokenize), made once per version.
        public IReadOnlyList<TjaSyntaxToken> Syntax => LazyInitializer.EnsureInitialized(ref syntax, () => TjaSyntax.Tokenize(Text));

        // Every #START, in file order.
        public IReadOnlyList<TjaDocumentBlock> Blocks
        {
            get
            {
                lock (gate)
                {
                    if (blocks != null) return blocks;
                    blocks = new List<TjaDocumentBlock>();
                    var positions = new List<LineSource>();
                    string course = "Oni";
                    int index = -1;
                    foreach (var line in TjaLines.Read(Text, positions))
                    {
                        index++;
                        if (line.Kind == TjaLineKind.Header && line.Key == "COURSE") course = line.Value;
                        if (line.Kind != TjaLineKind.Start) continue;
                        blocks.Add(new TjaDocumentBlock
                        {
                            Course = course, Side = ChartSide.OfStart(line.Value), Line = line.Line,
                            StartSpan = positions[index].Span(0, line.Text.Length),
                        });
                    }
                    return blocks;
                }
            }
        }

        // Every problem of the document: each block's, analysed as asked (Analyze), once each
        // where several blocks share one (a header before them all), in the order of the text.
        public List<TjaDiagnostic> Diagnostics(CancellationToken cancel = default)
        {
            var all = new List<TjaDiagnostic>();
            var seen = new HashSet<(string, int, int, string)>();
            var keys = new HashSet<(string, string)>();
            foreach (var block in Blocks)
            {
                if (!keys.Add((block.Course, block.Side))) continue;
                foreach (var d in Analyze(block.Course, block.Side, cancel).Diagnostics)
                    if (seen.Add((d.Code, d.Span.Start, d.Span.Length, d.Message))) all.Add(d);
            }
            // Stable: in the text's order, a block's own order where they meet.
            var ordered = new List<TjaDiagnostic>(all.Count);
            foreach (var d in System.Linq.Enumerable.OrderBy(all, d => d.Span.Start)) ordered.Add(d);
            return ordered;
        }

        // One block, as TjaParser.Analyze reads it. Threads may ask at once (a block asked for by
        // two may be read by both; one analysis is kept and given to both). A cancelled read
        // throws OperationCanceledException and keeps nothing: the next call reads the block.
        public TjaAnalysis Analyze(string course = "Oni", string side = "single", CancellationToken cancel = default)
        {
            var key = (course, side);
            TjaAnalysis old;
            lock (gate)
            {
                if (analyses.TryGetValue(key, out var done)) return done;
                earlier.TryGetValue(key, out old);
            }
            var analysis = old == null ? null : Moved(old, Text, AllLines, LineStarts, cancel);
            bool moved = analysis != null;
            if (!moved) analysis = TjaParser.Analyze(Text, course, side, cancel);
            analysis.Version = Version;
            analysis.Share(LineStarts);
            lock (gate)
            {
                if (analyses.TryGetValue(key, out var first)) return first;
                if (moved) Reused++; else Fresh++;
                analyses[key] = analysis;
                return analysis;
            }
        }

        // An earlier analysis moved into `text`, or null when the edit may have changed it. It is
        // moved only when every line that matters to it (ReadLines: every line up to its block's
        // end but the other blocks' bodies) is the same in `text`, character for character, and
        // has moved as the edit moves it.
        internal static TjaAnalysis Moved(TjaAnalysis old, string text, IReadOnlyList<TjaLine> textLines, TjaSourceText newLines, CancellationToken cancel = default)
        {
            if (old.Chart == null || old.ReadLines == null || old.StartLineIndex < 0) return null;
            string before = old.Text;
            // The edit: what lies between the common start and the common end.
            int start = 0, max = Math.Min(before.Length, text.Length);
            while (start < max && before[start] == text[start]) start++;
            int end = 0;
            while (end < max - start && before[before.Length - 1 - end] == text[text.Length - 1 - end]) end++;
            int oldEnd = before.Length - end, newEnd = text.Length - end, delta = text.Length - before.Length;
            if (start == before.Length && start == text.Length) return Copy(old, text, x => x, x => x, l => l, old.ReadLines);
            // An edit that makes or breaks a comment (a / or * in it or beside it) can change how any
            // later line reads: not reused.
            if (Touches(before, start, oldEnd) || Touches(text, start, newEnd)) return null;
            // An edit in or next to the block itself: read it again, without looking further.
            if (start <= old.EndSpan.End && oldEnd >= old.StartSpan.Start) return null;
            // A position moves with the text after the edit; a span's end (just past its last
            // character) at the edit's start stays, so text inserted right after a span is not in it.
            int Move(int offset)
            {
                if (offset < start) return offset;
                if (offset >= oldEnd) return offset + delta;
                throw new Unmovable();
            }
            int MoveEnd(int offset)
            {
                if (offset <= start) return offset;
                if (offset >= oldEnd) return offset + delta;
                throw new Unmovable();
            }
            var oldLines = old.Lines;

            // Walk the new text's lines beside the old relevant ones.
            var read = new List<TjaLine>();
            int k = 0, startIndex = -1;
            bool inBlock = false;
            try
            {
                foreach (var line in textLines)
                {
                    if ((k & 255) == 0) cancel.ThrowIfCancellationRequested();
                    if (k == old.ReadLines.Count) break;
                    // The other blocks' bodies do not matter (and were not kept).
                    if (line.Kind == TjaLineKind.Body && !inBlock) continue;
                    var was = old.ReadLines[k];
                    if (was.Kind != line.Kind || was.Implicit != line.Implicit || was.Text != line.Text
                        || !SameLine(before, oldLines, was.Line, text, newLines, line.Line, Move)) return null;
                    if (k == old.StartLineIndex) { inBlock = true; startIndex = read.Count; }
                    read.Add(line);
                    k++;
                    if (inBlock && line.Kind == TjaLineKind.End) break;
                }
                if (k != old.ReadLines.Count || startIndex < 0) return null;
                return Copy(old, text, Move, MoveEnd, l => l == 0 ? 0 : newLines.Line(Move(oldLines.LineStart(l))), read, startIndex);
            }
            catch (Unmovable) { return null; }
        }

        sealed class Unmovable : Exception { }

        static bool Touches(string text, int start, int end)
        {
            for (int i = Math.Max(0, start - 1); i < Math.Min(text.Length, end + 1); i++)
                if (text[i] == '/' || text[i] == '*') return true;
            return false;
        }

        // The same line: equal characters, and its start moved as the edit moves it.
        static bool SameLine(string before, TjaSourceText oldLines, int oldLine, string text, TjaSourceText newLines, int newLine, Func<int, int> move)
        {
            int a = oldLines.LineStart(oldLine), b = newLines.LineStart(newLine);
            if (move(a) != b) return false;
            int lengthA = LineLength(before, oldLines, oldLine), lengthB = LineLength(text, newLines, newLine);
            return lengthA == lengthB && string.CompareOrdinal(before, a, text, b, lengthA) == 0;
        }

        static int LineLength(string text, TjaSourceText lines, int line)
        {
            int start = lines.LineStart(line), end = line < lines.LineCount ? lines.LineStart(line + 1) : text.Length;
            return end - start;
        }

        // The analysis with its spans and lines moved; the warnings follow the diagnostics.
        static TjaAnalysis Copy(TjaAnalysis old, string text, Func<int, int> move, Func<int, int> moveEnd, Func<int, int> moveLine, List<TjaLine> read, int startIndex = -1)
        {
            // A span over several lines (a measure) takes in what was inserted in a line between.
            TextSpan Span(TextSpan span) => span.Length == 0 ? new TextSpan(move(span.Start), 0) : TextSpan.FromBounds(move(span.Start), moveEnd(span.End));
            var analysis = new TjaAnalysis(text, old.Course, old.Side)
            {
                StartSpan = Span(old.StartSpan), EndSpan = Span(old.EndSpan),
                ReadLines = read, StartLineIndex = startIndex < 0 ? old.StartLineIndex : startIndex,
                SourceMap = old.SourceMap.Moved(Span),
            };
            var warned = new HashSet<string>();
            var warnings = new List<string>();
            foreach (var d in old.Diagnostics)
            {
                var moved = new TjaDiagnostic(d.Kind, d.Message, ToArray(d.Args), moveLine(d.Line), Span(d.Span));
                analysis.diagnostics.Add(moved);
                if (moved.Severity == TjaSeverity.Warning && warned.Add(moved.Message)) warnings.Add(TjaParser.AtLine(moved.Message, moved.Line));
            }
            foreach (var m in old.Measures)
                analysis.measures.Add(new ChartMeasure
                {
                    Index = m.Index, Span = Span(m.Span), StartTime = m.StartTime, EndTime = m.EndTime, Bpm = m.Bpm, Length = m.Length,
                    Slots = m.Slots, BranchId = m.BranchId, Route = m.Route, Bar = m.Bar,
                });
            analysis.Chart = old.Chart.WithWarnings(warnings);
            return analysis;
        }

        static object[] ToArray(IReadOnlyList<object> args)
        {
            var array = new object[args.Count];
            for (int i = 0; i < array.Length; i++) array[i] = args[i];
            return array;
        }
    }
}
