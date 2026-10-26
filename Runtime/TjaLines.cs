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

    // The one line reader behind TjaParser, SongInfo, the local song search and the online
    // playable copy, so they agree on comments, case and block boundaries. Every Start gets an
    // End: as in OpenTaiko, a block without #END ends at the next COURSE:, #START or the file's end.
    public static class TjaLines
    {
        static readonly Regex CourseLine = new Regex(@"^COURSE\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static IEnumerable<TjaLine> Read(string text)
        {
            bool inBlock = false;
            int number = 0;
            foreach (var (n, uncommented) in Uncommented(text))
            {
                number = n;
                string line = uncommented.Trim();
                if (line.Length == 0) continue;
                bool start = line.StartsWith("#START", StringComparison.OrdinalIgnoreCase);
                if (inBlock && (start || CourseLine.IsMatch(line)))
                {
                    inBlock = false;
                    yield return new TjaLine(TjaLineKind.End, "#END", @implicit: true, line: number);
                }
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
            if (inBlock) yield return new TjaLine(TjaLineKind.End, "#END", @implicit: true, line: number);
        }

        // The file's lines, numbered from 1, without the BOM and comments. As in OpenTaiko, CRLF
        // and a lone CR end a line as LF does, and `//` runs to the end of its line. `/* … */`
        // (not in OpenTaiko) may span lines: within a line it leaves a space, so it still parts a
        // command from its argument; across lines, what is before it and what is after it stay
        // on their own lines; unclosed, it runs to the end of the file. Neither can be escaped,
        // so `TITLE:http://…` ends at `http:`, as in OpenTaiko.
        static IEnumerable<(int Number, string Text)> Uncommented(string text)
        {
            string[] raws = text.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var kept = new StringBuilder();
            bool inComment = false;
            for (int n = 0; n < raws.Length; n++)
            {
                string raw = raws[n];
                kept.Clear();
                for (int i = 0; i < raw.Length;)
                {
                    if (inComment)
                    {
                        int close = raw.IndexOf("*/", i, StringComparison.Ordinal);
                        if (close < 0) break;
                        inComment = false;
                        kept.Append(' ');
                        i = close + 2;
                        continue;
                    }
                    int line = raw.IndexOf("//", i, StringComparison.Ordinal);
                    int block = raw.IndexOf("/*", i, StringComparison.Ordinal);
                    if (block < 0 || (line >= 0 && line < block))
                    {
                        kept.Append(raw, i, (line < 0 ? raw.Length : line) - i);
                        break;
                    }
                    kept.Append(raw, i, block - i);
                    inComment = true;
                    i = block + 2;
                }
                yield return (n + 1, kept.ToString());
            }
        }
    }
}
