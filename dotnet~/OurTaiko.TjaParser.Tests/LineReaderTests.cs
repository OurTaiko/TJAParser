using System.Text;
using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// TjaLines reads lines in place since P2-1 (Documentation~/Parser-TODO.md). Random texts made of
// line breaks, comments, BOMs, Unicode spaces and block markers must give exactly the lines the
// earlier reader gave, which split a copy of the whole text.
public class LineReaderTests
{
    static readonly string[] Pieces =
    {
        "\r", "\n", "\r\n", "\n\r", "//", "/*", "*/", "/", "*", " ", "\t", "　", " ", "﻿",
        "#START", "#start P1", "#END", "#end", "COURSE:", "course :", "Course:Oni", "TITLE:http://x", "a", "1", ":", "P2", ",",
    };

    [Fact]
    public void ReadsAsTheEarlierReaderDid()
    {
        var r = new Random(42);
        for (int n = 0; n < 20000; n++)
        {
            var sb = new StringBuilder();
            if (r.Next(4) == 0) sb.Append('﻿');
            for (int k = r.Next(30); k > 0; k--) sb.Append(Pieces[r.Next(Pieces.Length)]);
            string text = sb.ToString();
            var expected = Legacy(text).Select(Show).ToList();
            var actual = TjaLines.Read(text).Select(Show).ToList();
            Assert.True(expected.SequenceEqual(actual), $"text {Escape(text)}:\n  expected {string.Join(" | ", expected)}\n  actual   {string.Join(" | ", actual)}");
        }
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("a", 1)]
    [InlineData("a\n", 2)]
    [InlineData("a\r\n", 2)]
    [InlineData("a\r", 2)]
    [InlineData("a\r\r\n", 3)]
    [InlineData("﻿﻿a\n\n", 3)]
    public void AnImplicitEndAtTheEndOfTheFileHasItsLastLine(string tail, int lines)
    {
        var last = TjaLines.Read("#START\n" + tail).Last();
        Assert.True(last.Implicit);
        Assert.Equal(lines + 1, last.Line);
    }

    static string Show(TjaLine l) => $"{l.Kind}:{Escape(l.Text)}:{l.Key}:{l.Value}:{l.Implicit}:{l.Line}";

    static string Escape(string s) => s == null ? "null" : string.Concat(s.Select(c => c < ' ' || c > '~' ? $"\\u{(int)c:X4}" : c.ToString()));

    // The reader before P2-1, kept as the reference.
    static IEnumerable<TjaLine> Legacy(string text)
    {
        var courseLine = new System.Text.RegularExpressions.Regex(@"^COURSE\s*:", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        bool inBlock = false;
        int number = 0;
        foreach (var (n, uncommented) in Uncommented(text))
        {
            number = n;
            string line = uncommented.Trim();
            if (line.Length == 0) continue;
            bool start = line.StartsWith("#START", StringComparison.OrdinalIgnoreCase);
            if (inBlock && (start || courseLine.IsMatch(line)))
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
