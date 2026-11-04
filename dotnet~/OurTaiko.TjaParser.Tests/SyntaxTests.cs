using System.Text;
using OurTaiko;
using OurTaiko.Corpus;
using Xunit;

namespace OurTaiko.Tests;

// I1-5 (Documentation~/Parser-TODO.md): the syntax tokens are the whole text, nothing left out,
// and they read the text as the parser does.
public class SyntaxTests
{
    static readonly string[] Pieces =
    {
        "1", "2", "0", "8", ",", "E", "e", "G", "7[ 1 0 ]", "9[", " ", "\t", "　", "\r\n", "\n", "\r", "/*", "*/", "//x", "/* c */",
        "#SCROLL 1", "#START", "#start P1", "#END", "COURSE:Oni", "TITLE: a b ", "BPM:1/*z*/20", "HELLO", "﻿",
    };

    static IEnumerable<string> Texts()
    {
        foreach (var (_, text) in GoldenTests.Corpus()) yield return text;
        var r = new Random(4);
        for (int n = 0; n < 3000; n++)
        {
            var sb = new StringBuilder(r.Next(4) == 0 ? "﻿" : "");
            for (int k = r.Next(40); k > 0; k--) sb.Append(Pieces[r.Next(Pieces.Length)]);
            yield return sb.ToString();
        }
    }

    [Fact]
    public void TheTokensAreTheText()
    {
        foreach (string text in Texts())
        {
            var tokens = TjaSyntax.Tokenize(text);
            int at = 0;
            var sb = new StringBuilder();
            foreach (var t in tokens)
            {
                Assert.Equal(at, t.Span.Start);
                Assert.True(t.Span.Length > 0);
                sb.Append(text, t.Span.Start, t.Span.Length);
                at = t.Span.End;
            }
            Assert.Equal(text, sb.ToString());
        }
    }

    [Fact]
    public void TheTokensReadAsTheParserReads()
    {
        int checkedTokens = 0;
        foreach (string text in Texts().Take(1200))
        {
            var tokens = TjaSyntax.Tokenize(text);
            var at = new Dictionary<int, TjaSyntaxToken>();
            foreach (var t in tokens) at[t.Span.Start] = t;
            // Every token of every block the parser reads.
            foreach (string course in SongInfo.CourseNames)
            {
                var sources = new List<LineSource>();
                var parser = new TjaParser.ChartParser(sources, new List<TjaDiagnostic>());
                try { parser.Run(text, TjaLines.Read(text, sources), course, "single"); }
                catch (ArgumentException) { continue; }
                for (int i = 0; i < parser.Tokens.Count; i++)
                {
                    string token = parser.Tokens[i];
                    var syntax = at[parser.TokenSpans[i].Start];
                    var expected = token[0] == '#' ? TjaSyntaxKind.Command : token == "," ? TjaSyntaxKind.Comma : token[0] == '0' ? TjaSyntaxKind.Blank
                        : token[0] == '8' ? TjaSyntaxKind.RollEnd : "1234567CFG9".IndexOf(token[0]) >= 0 ? TjaSyntaxKind.Note : TjaSyntaxKind.Unknown;
                    // A # amid a note line is a token of its own to the parser (read as a command
                    // and warned about); to the tokens it is a character of that note line.
                    if (token == "#" && syntax.Kind == TjaSyntaxKind.Unknown) continue;
                    Assert.Equal(expected, syntax.Kind);
                    if (token.Length > 1 && token[0] != '#') Assert.Equal(TjaSyntaxKind.InlineCount, at[text.IndexOf('[', parser.TokenSpans[i].Start)].Kind);
                    checkedTokens++;
                }
            }
            // Every header: its key and its value.
            var positions = new List<LineSource>();
            int index = -1;
            foreach (var line in TjaLines.Read(text, positions))
            {
                index++;
                if (line.Kind != TjaLineKind.Header) continue;
                Assert.Equal(TjaSyntaxKind.HeaderKey, at[positions[index].SourceOffset(0)].Kind);
                int value = line.Text.IndexOf(':') + 1;
                while (value < line.Text.Length && char.IsWhiteSpace(line.Text[value])) value++;
                if (value < line.Text.Length) Assert.Equal(TjaSyntaxKind.HeaderValue, at[positions[index].SourceOffset(value)].Kind);
            }
        }
        Assert.True(checkedTokens > 100000, checkedTokens.ToString());
    }

    [Fact]
    public void ALineReadsAsItsParts()
    {
        string text = "﻿TITLE: My Song // note\r\n#START P1\n1G08, 7[5]8,\n#SCROLL /* x */ 2\n#END";
        var tokens = TjaSyntax.Tokenize(text);
        Assert.Equal(new[]
        {
            "Bom ﻿", "HeaderKey TITLE", "Colon :", "Whitespace  ", "HeaderValue My Song", "Whitespace  ", "Comment // note", "NewLine \r\n",
            "Command #START", "Whitespace  ", "Argument P1", "NewLine \n",
            "Note 1", "Note G", "Blank 0", "RollEnd 8", "Comma ,", "Whitespace  ", "Note 7", "InlineCount [5]", "RollEnd 8", "Comma ,", "NewLine \n",
            "Command #SCROLL", "Whitespace  ", "Comment /* x */", "Whitespace  ", "Argument 2", "NewLine \n", "Command #END",
        }, tokens.Select(t => t.Kind + " " + text.Substring(t.Span.Start, t.Span.Length)));
        Assert.Equal(new NoteKind?[] { NoteKind.Don, NoteKind.Kadon, NoteKind.Balloon }, tokens.Where(t => t.Kind == TjaSyntaxKind.Note).Select(t => t.Note));
    }
}
