using System.Text.Json.Nodes;
using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// I3-1 (Documentation~/Parser-TODO.md): a fix makes its diagnostic go, and the chart is what the
// fix means: the same as read before (a missing comma or #END, recovered from), or with the command
// it meant.
public class QuickFixTests
{
    static string Block(string body, string end = "#END\n") => "BPM:120\nBALLOON:5\nCOURSE:Oni\nLEVEL:9\n#START\n" + body + end;

    [Theory]
    [InlineData("TJA2003", "1,\n12\n", "#END\n")]
    [InlineData("TJA1007", "11\n#BRANCHSTART p,1,2\n#N\n1,\n#E\n2,\n#M\n3,\n#BRANCHEND\n", "#END\n")]
    [InlineData("TJA2001", "1,\n", "")]
    [InlineData("TJA2001", "1,", "")]
    public void AFixKeepsTheChartAsItWasRead(string code, string body, string end)
    {
        string text = Block(body, end);
        var document = new TjaDocument(text);
        var d = document.Diagnostics().Single(x => x.Code == code);
        string fixedText = TjaTextEdit.Apply(text, TjaQuickFix.For(document, d).Single().Edits);
        var after = TjaParser.Analyze(fixedText);
        Assert.Empty(after.Diagnostics);
        Assert.Equal(ChartHash.Of(TjaParser.Analyze(text).Chart), ChartHash.Of(after.Chart));
    }

    [Fact]
    public void ASlipOfACommandIsSpeltRight()
    {
        string text = Block("#GOGOSTRAT\n1,\n#scrol 2\n1,\n");
        var document = new TjaDocument(text);
        var fixes = document.Diagnostics().Where(d => d.Kind == TjaDiagnosticKind.SimilarCommand).SelectMany(d => TjaQuickFix.For(document, d)).ToList();
        Assert.Equal(new[] { "Change to #GOGOSTART", "Change to #SCROLL" }, fixes.Select(f => f.Title));
        string fixedText = TjaTextEdit.Apply(text, fixes.SelectMany(f => f.Edits));
        Assert.Equal(Block("#GOGOSTART\n1,\n#SCROLL 2\n1,\n"), fixedText);
        Assert.Empty(new TjaDocument(fixedText).Diagnostics());
    }

    [Fact]
    public void BalloonCountsGoOnTheBalloonsAndPlayTheSame()
    {
        string text = Block("7008,\n9[12]008,\n7008,\n");
        var document = new TjaDocument(text);
        var action = TjaQuickFix.InlineBalloons(document, "Oni", "single");
        string fixedText = TjaTextEdit.Apply(text, action.Edits);
        Assert.Equal(Block("7[5]008,\n9[12]008,\n7[5]008,\n"), fixedText);
        Assert.Equal(ChartHash.Of(TjaParser.Parse(text)), ChartHash.Of(TjaParser.Parse(fixedText)));
        Assert.Null(TjaQuickFix.InlineBalloons(new TjaDocument(fixedText), "Oni", "single"));
    }

    [Fact]
    public async Task TheServerOffersTheFixes()
    {
        await using var client = LspClient.StartInProcess();
        await client.Request("initialize", LspClient.Initialize());
        const string uri = "file:///q.tja";
        await client.Notify("textDocument/didOpen", LspClient.Open(uri, Block("#GOGOSTRAT\n7008,\n")));
        var answer = await client.Request("textDocument/codeAction", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["range"] = new JsonObject { ["start"] = new JsonObject { ["line"] = 5, ["character"] = 1 }, ["end"] = new JsonObject { ["line"] = 5, ["character"] = 1 } },
        });
        Assert.Equal(new[] { "Change to #GOGOSTART", "Write the balloon counts on the balloons" }, answer["result"].AsArray().Select(a => (string)a["title"]));
        Assert.Equal("#GOGOSTART", (string)answer["result"][0]["edit"]["changes"][uri][0]["newText"]);
        await client.Request("shutdown");
        Assert.Equal(0, await client.Exit());
    }
}
