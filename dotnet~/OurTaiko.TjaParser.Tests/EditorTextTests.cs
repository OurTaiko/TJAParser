using System.Text.Json.Nodes;
using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// E3 (Documentation~/Parser-TODO.md): the editor features' words in Chinese and Japanese — the
// schema's summaries, hovers, completions, code actions and block summaries. English is unchanged.
public class EditorTextTests
{
    static bool HasCjk(string s) => s.Any(c => c >= 0x3040 && c <= 0x9FFF);

    [Fact]
    public void EveryKeywordIsSummarisedInEveryLanguage()
    {
        foreach (var k in TjaSchema.Commands.Concat(TjaSchema.Headers))
        {
            Assert.Equal(k.Summary, k.SummaryIn("en"));
            Assert.Equal(k.Summary, k.SummaryIn(null));
            Assert.True(HasCjk(k.SummaryIn("zh-cn")), k.Name + " zh");
            Assert.True(HasCjk(k.SummaryIn("ja")), k.Name + " ja");
        }
        Assert.Equal("Oni 难度的谱面作者。", TjaSchema.Find("NOTESDESIGNER3").SummaryIn("zh"));
    }

    [Fact]
    public void HoversSayItInTheLanguageAsked()
    {
        var document = new TjaDocument("BPM:120\nCOURSE:Oni\nLEVEL:9\n#START\n1020,\n#SCROLL 2\n7008,\n#END\n");
        int ka = document.Text.IndexOf("1020") + 2;
        Assert.StartsWith("**Ka** カッ", TjaHover.At(document, ka).Markdown);
        string zh = TjaHover.At(document, ka, language: "zh").Markdown;
        Assert.StartsWith("**咔** カッ", zh);
        Assert.Contains("时刻 1 秒", zh);
        Assert.Contains("第 1 小节，4 格中的第 3 格", zh);
        string ja = TjaHover.At(document, document.Text.IndexOf("7008"), language: "ja").Markdown;
        Assert.Contains("**ふうせん**", ja);
        Assert.Contains("5 打", ja);
        string end = TjaHover.At(document, document.Text.IndexOf("7008") + 3, language: "zh").Markdown;
        Assert.StartsWith("**气球的结尾**", end);
        string scroll = TjaHover.At(document, document.Text.IndexOf("#SCROLL") + 2, language: "ja").Markdown;
        Assert.Contains("スクロール速度を変える", scroll);
        Assert.Contains("演奏内容が変わる · 仕様 §6.3", scroll);
    }

    [Fact]
    public void CompletionsActionsAndSummariesFollow()
    {
        string text = "BPM:120\nCOURSE:Oni\nLEVEL:9\n#START\n1020\n#END\n";
        var document = new TjaDocument(text);
        var notes = TjaCompletion.At(document, text.IndexOf("1020"), language: "ja");
        Assert.Equal("ドン", notes.Single(i => i.Label == "1").Detail);
        Assert.Equal("連打の終わり", notes.Single(i => i.Label == "8").Detail);
        Assert.Equal("Don", TjaCompletion.At(document, text.IndexOf("1020")).Single(i => i.Label == "1").Detail);
        var headers = TjaCompletion.At(new TjaDocument("WA"), 2, language: "zh");
        Assert.Equal("音频文件。", headers.Single(i => i.Label == "WAVE").Detail);

        var missing = document.Diagnostics().Single(d => d.Kind == TjaDiagnosticKind.LastMeasureWithoutComma);
        Assert.Equal("在小节末尾加上逗号", TjaQuickFix.For(document, missing, "zh").Single().Title);
        Assert.Equal("End the measure with a comma", TjaQuickFix.For(document, missing).Single().Title);

        var fixedDocument = new TjaDocument("BPM:120\nCOURSE:Oni\nLEVEL:9\n#START\n1020,\n#END\n");
        var block = fixedDocument.Blocks[0];
        Assert.Equal("ここで小節を分ける", TjaRefactor.Split(fixedDocument, block.Course, block.Side, fixedDocument.Text.IndexOf("1020") + 2, language: "ja").Title);
        var summary = TjaSummary.Blocks(fixedDocument).Single();
        Assert.StartsWith("2 个音符 · 最大连击 2 · ", summary.TitleIn("zh"));
        Assert.Equal(summary.Title, summary.TitleIn("en"));
    }

    [Fact]
    public async Task TheServersHoverAndCompletionUseTheClientsLanguage()
    {
        await using var client = LspClient.StartInProcess();
        var init = LspClient.Initialize();
        init["locale"] = "zh-cn";
        await client.Request("initialize", init);
        const string uri = "file:///z.tja";
        await client.Notify("textDocument/didOpen", LspClient.Open(uri, "BPM:120\nCOURSE:Oni\nLEVEL:9\n#START\n1020,\n#END\n"));
        var hover = await client.Request("textDocument/hover", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri }, ["position"] = new JsonObject { ["line"] = 4, ["character"] = 2 },
        });
        Assert.StartsWith("**咔** カッ", (string)hover["result"]["contents"]["value"]);
        var completion = await client.Request("textDocument/completion", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri }, ["position"] = new JsonObject { ["line"] = 4, ["character"] = 0 },
        });
        var items = completion["result"] is JsonArray array ? array : (JsonArray)completion["result"]["items"];
        Assert.Contains(items, i => (string)i["label"] == "1" && (string)i["detail"] == "咚");
        await client.Request("shutdown");
        Assert.Equal(0, await client.Exit());
    }
}
