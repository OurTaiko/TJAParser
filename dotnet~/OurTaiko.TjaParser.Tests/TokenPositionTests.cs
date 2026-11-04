using System.Text;
using OurTaiko;
using OurTaiko.Corpus;
using Xunit;

namespace OurTaiko.Tests;

// I0-1 (Documentation~/Parser-TODO.md): with positions tracked, every token of a block knows its
// span in the file. Cut from the text, the span is the token: a note character, a balloon with
// its inline count, a command line (comments inside a span are the only difference allowed).
public class TokenPositionTests
{
    static readonly string[] Pieces =
    {
        "1", "2", "0", "3", "5", "8", ",", " ", "\t", "　", "/*c*/", "/*", "*/", "//x", "7[ 1 0 ]", "9[5]",
        "#SCROLL 1.5", "#GOGOSTART", "#BPMCHANGE 150", "#SCROLL /* z */ 2", "\r\n", "\n", "\r", "\n", "\n",
    };

    static IEnumerable<string> Texts()
    {
        yield return Tja.Fixture("Donkama2000.tja");
        yield return Tja.Fixture("IrohaUta.tja");
        yield return Tja.Sample;
        yield return Tja.Branching;
        for (int seed = 0; seed < 60; seed++) yield return ChartCorpus.Generate(50000 + seed, 8, messy: seed % 2 == 1);
        for (int seed = 0; seed < 200; seed++) yield return ChartCorpus.Mutate(ChartCorpus.Generate(51000 + seed % 40, 8, messy: true), seed);
        var r = new Random(3);
        for (int n = 0; n < 1500; n++)
        {
            var sb = new StringBuilder(r.Next(3) == 0 ? "﻿" : "");
            sb.Append(r.Next(2) == 0 ? "BPM:120\r\nCOURSE:Oni\r\n#START\r\n" : "BPM:120\nCOURSE:Oni\n#START\n");
            for (int k = r.Next(60); k > 0; k--) sb.Append(Pieces[r.Next(Pieces.Length)]);
            sb.Append("\n#END\n");
            yield return sb.ToString();
        }
    }

    static TjaParser.ChartParser Tokens(string text, string course, bool track)
    {
        var sources = track ? new List<LineSource>() : null;
        var parser = new TjaParser.ChartParser(sources);
        try { parser.Run(text, TjaLines.Read(text, sources), course, "single"); }
        catch (FormatException) { }
        catch (NotSupportedException) { }
        return parser;
    }

    [Fact]
    public void EveryTokenIsWhereItsSpanSays()
    {
        int checkedTokens = 0;
        foreach (string text in Texts())
        {
            var lines = new TjaSourceText(text);
            foreach (string course in SongInfo.CourseNames)
            {
                var parser = Tokens(text, course, track: true);
                // Tracking changes nothing of what is read.
                Assert.Equal(Tokens(text, course, track: false).Tokens, parser.Tokens);
                for (int i = 0; i < parser.Tokens.Count; i++)
                {
                    string token = parser.Tokens[i];
                    var span = parser.TokenSpans[i];
                    string cut = text.Substring(span.Start, span.Length);
                    string where = $"token {i} '{token}' at {span} cut '{cut}' in:\n{text}";
                    Assert.True(lines.Line(span.Start) == parser.TokenLines[i], where);
                    if (cut.Contains("/*")) Assert.True(cut[0] == token[0] && cut[^1] == token[^1], where);
                    else if (token[0] == '#') Assert.True(cut == token, where);
                    else Assert.True(new string(cut.Where(c => !char.IsWhiteSpace(c)).ToArray()) == token, where);
                    checkedTokens++;
                }
            }
        }
        Assert.True(checkedTokens > 100000, checkedTokens.ToString());
    }

    [Theory]
    [InlineData("a\r\nb\rc\nd", 0, 1, 0)]
    [InlineData("a\r\nb\rc\nd", 3, 2, 0)]
    [InlineData("a\r\nb\rc\nd", 5, 3, 0)]
    [InlineData("a\r\nb\rc\nd", 7, 4, 0)]
    [InlineData("﻿ab\n", 2, 1, 2)]
    [InlineData("ab\n", 3, 2, 0)]
    public void LinesAndColumnsFollowTheLineBreaksTjaLinesReads(string text, int offset, int line, int column)
    {
        var source = new TjaSourceText(text);
        Assert.Equal((line, column), source.Position(offset));
        Assert.Equal(offset, source.Offset(line, column));
    }
}
