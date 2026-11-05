using System.Text;
using System.Text.Json.Nodes;
using OurTaiko;
using OurTaiko.Corpus;
using OurTaiko.LanguageServer;
using Xunit;

namespace OurTaiko.Tests;

// I2-6 (Documentation~/Parser-TODO.md): folding by blocks, branches, routes and comments; semantic
// tokens that cover every character but whitespace, without overlapping.
public class FoldingAndHighlightTests
{
    static readonly string[] Pieces =
    {
        "1", "2", "3", "4", "5", "6", "7", "9", "0", "8", ",", "E", "G", "C", "F", "7[ 1 0 ]", " ", "\t", "　", "\r\n", "\n", "\r", "/*", "*/", "//x",
        "#SCROLL 1", "#START", "#END", "COURSE:Oni", "TITLE: a b ", "HELLO", "﻿", "#BRANCHSTART p,1,2", "#N", "#E", "#M", "#BRANCHEND",
    };

    static IEnumerable<string> Texts()
    {
        foreach (var (_, text) in GoldenTests.Corpus().Take(150)) yield return text;
        var r = new Random(6);
        for (int n = 0; n < 2000; n++)
        {
            var sb = new StringBuilder(r.Next(5) == 0 ? "﻿" : "");
            for (int k = r.Next(50); k > 0; k--) sb.Append(Pieces[r.Next(Pieces.Length)]);
            yield return sb.ToString();
        }
    }

    [Fact]
    public void SemanticTokensCoverEveryCharacterButWhitespaceOnce()
    {
        foreach (string text in Texts())
        {
            var document = new TjaDocument(text);
            var data = SemanticTokens.Encode(document);
            var covered = new bool[text.Length];
            var syntax = document.Syntax.ToDictionary(t => t.Span.Start);
            int line = 0, column = 0, previousEnd = -1;
            for (int i = 0; i < data.Count; i += 5)
            {
                line += data[i];
                column = data[i] == 0 ? column + data[i + 1] : data[i + 1];
                int start = document.Lines.Offset(line + 1, column), length = data[i + 2];
                Assert.True(start >= previousEnd, "tokens overlap or go back");
                previousEnd = start + length;
                // It is a syntax token, whole, of the type its kind has.
                Assert.True(syntax.TryGetValue(start, out var token) && token.Span.Length == length);
                Assert.Equal(SemanticTokens.Type(token), data[i + 3]);
                for (int c = start; c < start + length; c++) covered[c] = true;
            }
            for (int c = 0; c < text.Length; c++)
                Assert.True(covered[c] || char.IsWhiteSpace(text[c]) || text[c] == '﻿', $"character {c} '{text[c]}' is in no token");
        }
    }

    [Fact]
    public void NotesHaveTypesOfTheirOwn()
    {
        var document = new TjaDocument("COURSE:Oni\n#START\n1234G08,\n#END\n");
        var types = SemanticTokens.Encode(document).Where((_, i) => i % 5 == 3).Select(t => SemanticTokens.Types[t]).ToList();
        Assert.Equal(new[] { "property", "operator", "string", "keyword", "don", "ka", "bigDon", "bigKa", "kadon", "blank", "rollEnd", "operator", "keyword" }, types);
    }

    [Fact]
    public void BlocksBranchesRoutesAndCommentsFold()
    {
        string text = string.Join("\n", "BPM:120", "COURSE:Oni", "#START", "1,", "#BRANCHSTART p,1,2", "#N", "1,", "#E", "2,", "2,", "#M", "3,",
            "#BRANCHEND", "/* a", "b */", "1,", "#END", "COURSE:Easy", "#START", "1,", "1,", "COURSE:Hard") + "\n";
        Assert.Equal(new[] { "Block 3-17", "Branch 5-13", "Route 6-7", "Route 8-10", "Route 11-12", "Comment 14-15", "Block 19-21" },
            TjaFolding.Ranges(new TjaDocument(text)).Select(r => r.ToString()));
        // A branch without #BRANCHEND ends with its block; a comment never closed, at the end.
        string open = "COURSE:Oni\n#START\n#BRANCHSTART p,1,2\n#N\n1,\n#E\n1,\n#END\n/* x\ny\n";
        Assert.Equal(new[] { "Block 2-8", "Branch 3-7", "Route 4-5", "Route 6-7", "Comment 9-11" }, TjaFolding.Ranges(new TjaDocument(open)).Select(r => r.ToString()));
    }

    [Fact]
    public async Task TheServerAnswersFoldingAndSemanticTokens()
    {
        await using var client = LspClient.StartInProcess();
        var init = await client.Request("initialize", LspClient.Initialize());
        Assert.Contains("don", init["result"]["capabilities"]["semanticTokensProvider"]["legend"]["tokenTypes"].AsArray().Select(t => (string)t));
        const string uri = "file:///f.tja";
        await client.Notify("textDocument/didOpen", LspClient.Open(uri, "COURSE:Oni\n#START\n1,\n#END\n"));
        var tokens = await client.Request("textDocument/semanticTokens/full", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } });
        Assert.Equal(7 * 5, tokens["result"]["data"].AsArray().Count);
        var folds = await client.Request("textDocument/foldingRange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } });
        Assert.Equal((1, 3), ((int)folds["result"][0]["startLine"], (int)folds["result"][0]["endLine"]));
        await client.Request("shutdown");
        Assert.Equal(0, await client.Exit());
    }
}
