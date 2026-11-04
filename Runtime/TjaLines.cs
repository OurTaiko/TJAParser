using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace OurTaiko
{
    public enum TjaLineKind
    {
        Header,  // KEY:VALUE outside a chart block
        Start,   // #START, with its player (P1, P2) as Value
        End,     // #END, or where a block without one ends (Implicit)
        Body,    // a note line or command inside #START … #END
        Other,   // anything else outside a block
    }

    // One meaningful line of a TJA file: without the BOM, `//` and `/* */` comments or
    // surrounding spaces; empty lines are skipped. #START and #END are recognised in any case.
    public readonly struct TjaLine
    {
        public readonly TjaLineKind Kind;
        public readonly string Text;
        // A header's upper-case key and trimmed value; #START's player.
        public readonly string Key, Value;
        // An End the file did not write.
        public readonly bool Implicit;
        // 1-based line of the file; an implicit End has the line that ended the block.
        public readonly int Line;

        public TjaLine(TjaLineKind kind, string text, string key = null, string value = null, bool @implicit = false, int line = 0)
        {
            Kind = kind; Text = text; Key = key; Value = value; Implicit = @implicit; Line = line;
        }
    }

    // Where a line's text stands in the file (TjaLines.Read with positions, for analysis): Text[i]
    // is at SourceOffset(i). A line without comments is one stretch of the file; a line with
    // comments is several, kept as pairs (index in Text, offset in the file). An implicit #END has
    // no text and stands where the line that ended its block starts (or at the end of the file).
    internal readonly struct LineSource
    {
        public readonly int Offset;
        readonly int[] pieces;

        public LineSource(int offset, int[] pieces = null)
        {
            Offset = offset; this.pieces = pieces;
        }

        public int SourceOffset(int index)
        {
            if (pieces == null) return Offset + index;
            int k = 0;
            while (k + 2 < pieces.Length && pieces[k + 2] <= index) k += 2;
            return pieces[k + 1] + (index - pieces[k]);
        }

        // The file span of Text[from..to): from its first to its last character (comments inside
        // included); empty where `from` stands when there are none.
        public TextSpan Span(int from, int to)
            => to <= from ? new TextSpan(SourceOffset(from), 0) : TextSpan.FromBounds(SourceOffset(from), SourceOffset(to - 1) + 1);
    }

    // The one line reader behind TjaParser, SongInfo, the local song search and the online
    // playable copy, so they agree on comments, case and block boundaries. Every Start gets an
    // End: as in OpenTaiko, a block without #END ends at the next COURSE:, #START or the file's end.
    public static class TjaLines
    {
        static readonly Regex CourseLine = new Regex(@"^COURSE\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Only a line starting with c or C can match CourseLine (no other character is c in any
        // case), so the regex runs for those alone and not for every note line of a block.
        static bool IsCourseLine(string line) => (line[0] == 'C' || line[0] == 'c') && CourseLine.IsMatch(line);

        // `#START` in any case, then anything but a letter: OpenTaiko reads a command's name as `#`
        // and the capital letters after it, so `#STARTP1` is another command, not a #START.
        static bool IsStart(string line)
            => line.StartsWith("#START", StringComparison.OrdinalIgnoreCase)
                && (line.Length == 6 || !(line[6] >= 'A' && line[6] <= 'Z' || line[6] >= 'a' && line[6] <= 'z'));

        public static IEnumerable<TjaLine> Read(string text) => Read(text, null);

        // With `positions`, each line's place in the file is added to it as the line is read (so
        // positions[k] is the k-th line's once that line has come).
        internal static IEnumerable<TjaLine> Read(string text, List<LineSource> positions)
        {
            bool inBlock = false;
            int number = 0;
            var lines = new LineReader(text, positions != null);
            while (lines.Next(out string line))
            {
                number = lines.Number;
                if (line.Length == 0) continue;
                bool start = IsStart(line);
                if (inBlock && (start || IsCourseLine(line)))
                {
                    inBlock = false;
                    positions?.Add(new LineSource(lines.Source.Offset));
                    yield return new TjaLine(TjaLineKind.End, "#END", @implicit: true, line: number);
                }
                positions?.Add(lines.Source);
                if (start)
                {
                    inBlock = true;
                    yield return new TjaLine(TjaLineKind.Start, line, value: line.Substring(6).Trim().ToUpperInvariant(), line: number);
                }
                else if (line.Equals("#END", StringComparison.OrdinalIgnoreCase))
                {
                    inBlock = false;
                    yield return new TjaLine(TjaLineKind.End, line, line: number);
                }
                else if (inBlock) yield return new TjaLine(TjaLineKind.Body, line, line: number);
                else
                {
                    int colon = line.IndexOf(':');
                    yield return colon < 0 ? new TjaLine(TjaLineKind.Other, line, line: number)
                        : new TjaLine(TjaLineKind.Header, line, line.Substring(0, colon).Trim().ToUpperInvariant(), line.Substring(colon + 1).Trim(), line: number);
                }
            }
            if (inBlock)
            {
                positions?.Add(new LineSource(text.Length));
                yield return new TjaLine(TjaLineKind.End, "#END", @implicit: true, line: number);
            }
        }

        // The file's lines, numbered from 1, without the BOM and comments, and trimmed. As in
        // OpenTaiko, CRLF and a lone CR end a line as LF does, and `//` runs to the end of its line.
        // `/* … */` (not in OpenTaiko) may span lines: within a line it leaves a space, so it still
        // parts a command from its argument; across lines, what is before it and what is after it
        // stay on their own lines; unclosed, it runs to the end of the file. Neither can be
        // escaped, so `TITLE:http://…` ends at `http:`, as in OpenTaiko. The lines are read from
        // the text in place: only what is kept of each line becomes a string.
        sealed class LineReader
        {
            static readonly char[] Breaks = { '\r', '\n' };
            readonly string text;
            readonly StringBuilder kept = new StringBuilder();
            // Whether to note where each line's text stands (Source); the pieces of a commented
            // line as (index in kept, offset in the file).
            readonly bool track;
            readonly List<int> pieces;
            public LineSource Source;
            // Where the next line starts; past the end once the last line has been read.
            int next;
            bool inComment;
            public int Number;

            public LineReader(string text, bool track = false)
            {
                this.text = text;
                this.track = track;
                if (track) pieces = new List<int>();
                while (next < text.Length && text[next] == '\uFEFF') next++;
            }

            // The next line; false after the last, which is the (maybe empty) text after the final break.
            public bool Next(out string line)
            {
                line = null;
                if (next > text.Length) return false;
                int start = next, end = text.IndexOfAny(Breaks, start);
                if (end < 0) { end = text.Length; next = text.Length + 1; }
                else next = end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
                Number++;
                line = Uncomment(start, end);
                return true;
            }

            string Uncomment(int start, int end)
            {
                // Most lines have no comment: they are cut, trimmed, straight from the text.
                if (!inComment && text.IndexOf('/', start, end - start) < 0) return Trimmed(start, end);
                kept.Clear();
                pieces?.Clear();
                for (int i = start; i < end;)
                {
                    if (inComment)
                    {
                        int close = text.IndexOf("*/", i, end - i, StringComparison.Ordinal);
                        if (close < 0) break;
                        inComment = false;
                        Piece(close);
                        kept.Append(' ');
                        i = close + 2;
                        continue;
                    }
                    int line = text.IndexOf("//", i, end - i, StringComparison.Ordinal);
                    int block = text.IndexOf("/*", i, end - i, StringComparison.Ordinal);
                    if (block < 0 || (line >= 0 && line < block))
                    {
                        Piece(i);
                        kept.Append(text, i, (line < 0 ? end : line) - i);
                        break;
                    }
                    Piece(i);
                    kept.Append(text, i, block - i);
                    inComment = true;
                    i = block + 2;
                }
                string result = kept.ToString().Trim();
                if (track)
                {
                    // The trimmed line starts this many characters into what was kept.
                    int lead = 0;
                    while (lead < kept.Length && char.IsWhiteSpace(kept[lead])) lead++;
                    var map = new int[pieces.Count];
                    for (int k = 0; k < map.Length; k += 2) { map[k] = pieces[k] - lead; map[k + 1] = pieces[k + 1]; }
                    // A line all comment keeps nothing: it stands where it starts.
                    Source = map.Length == 0 ? new LineSource(start) : new LineSource(new LineSource(0, map).SourceOffset(0), map);
                }
                return result;
            }

            // A kept piece of a commented line starts here in the file.
            void Piece(int offset)
            {
                if (!track) return;
                pieces.Add(kept.Length);
                pieces.Add(offset);
            }

            // As string.Trim would give it.
            string Trimmed(int start, int end)
            {
                while (start < end && char.IsWhiteSpace(text[start])) start++;
                while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
                if (track) Source = new LineSource(start);
                return start == end ? "" : text.Substring(start, end - start);
            }
        }
    }
}
