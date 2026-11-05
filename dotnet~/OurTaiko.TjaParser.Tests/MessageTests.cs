using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// I3-5 (Documentation~/Parser-TODO.md): every diagnostic in Chinese and Japanese, with the same
// placeholders as in English; Parse and Warnings stay in English.
public class MessageTests
{
    static string[] Holes(string format) => Regex.Matches(format, @"\{\d\}").Select(m => m.Value).Distinct().OrderBy(h => h).ToArray();

    [Fact]
    public void EveryKindIsTranslatedWithItsPlaceholders()
    {
        foreach (var kind in TjaDiagnosticKind.All)
        {
            Assert.True(TjaMessages.Has(kind.Code, out string zh, out string ja), kind.Code);
            Assert.Equal(Holes(kind.Format), Holes(zh));
            Assert.Equal(Holes(kind.Format), Holes(ja));
        }
    }

    [Fact]
    public void ADiagnosticSaysItInTheLanguageAsked()
    {
        string text = "BPM:120\nCOURSE:Oni\n#START\n#FOO\n1,\n#END\n";
        var d = new TjaDocument(text).Diagnostics();
        var unknown = d.Single(x => x.Kind == TjaDiagnosticKind.UnknownCommand);
        Assert.Equal("未知指令：#FOO", unknown.MessageIn("zh-cn"));
        Assert.Equal("不明な命令：#FOO", unknown.MessageIn("ja"));
        Assert.Equal("#FOO", unknown.MessageIn("en"));
        Assert.Equal("#FOO", unknown.MessageIn(null));
        Assert.Equal("缺少 LEVEL。", d.Single(x => x.Kind == TjaDiagnosticKind.LevelOutOfRange).MessageIn("zh-tw"));
        // The parser's own words are English.
        Assert.Equal(new[] { "#FOO (line 4)" }, TjaParser.Parse(text).Warnings);
    }

    [Fact]
    public async Task TheServerSpeaksTheClientsLanguage()
    {
        await using var client = LspClient.StartInProcess();
        var init = LspClient.Initialize();
        init["locale"] = "ja";
        await client.Request("initialize", init);
        const string uri = "file:///m.tja";
        await client.Notify("textDocument/didOpen", LspClient.Open(uri, "BPM:120\nCOURSE:Oni\nLEVEL:9\n#START\n#FOO\n1,\n#END\n"));
        Assert.Equal("不明な命令：#FOO", (string)(await client.Diagnostics(uri, 1))[0]["message"]);
        await client.Request("shutdown");
        Assert.Equal(0, await client.Exit());
    }
}
