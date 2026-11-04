using System.Text.Json.Nodes;
using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// I2-5 (Documentation~/Parser-TODO.md): while a command's arguments are written, the parameter
// the position is on is the one shown. `|` marks the position.
public class SignatureHelpTests
{
    static TjaSignature At(string line)
    {
        string text = "BPM:120\nCOURSE:Oni\nLEVEL:9\n#START\n" + line.Replace("|", "") + "\n1,\n#END\n";
        return TjaSignatureHelp.At(new TjaDocument(text), text.IndexOf(line.Substring(0, line.IndexOf('|')), StringComparison.Ordinal) + line.IndexOf('|'));
    }

    [Theory]
    [InlineData("#JPOSSCROLL |", "<seconds>")]
    [InlineData("#JPOSSCROLL 1.5|", "<seconds>")]
    [InlineData("#JPOSSCROLL 1.5 |", "<move>")]
    [InlineData("#JPOSSCROLL 1.5 -20|", "<move>")]
    [InlineData("#JPOSSCROLL 1.5 -20 |", "[direction]")]
    [InlineData("#JPOSSCROLL 1.5  -20 0 4 |", "[direction]")]
    [InlineData("#SUDDEN 2 |", "<move>")]
    [InlineData("#LANE_ALPHA 50 |", "[ms]")]
    [InlineData("#BRANCHSTART p|", "<type>")]
    [InlineData("#BRANCHSTART p, 50|", "<expert>")]
    [InlineData("#BRANCHSTART p,50,80|", "<master>")]
    [InlineData("#BRANCHSTART p,50,80,|", "[l|m]")]
    [InlineData("#SCROLL 1 + 2i|", "<speed>")]
    [InlineData("#bpmchange 1|", "<bpm>")]
    public void TheParameterWrittenIsTheOneShown(string line, string parameter)
    {
        var signature = At(line);
        var (start, length) = signature.Parameters[signature.Active];
        Assert.Equal(parameter, signature.Label.Substring(start, length));
    }

    [Theory]
    [InlineData("#GOGOSTART |")]
    [InlineData("#BPMCHANGE|")]
    [InlineData("1010 |")]
    [InlineData("#FOO 1 |")]
    [InlineData("#SCROLL 1 // |")]
    public void NoSignatureWhereNoParameterIsWritten(string line) => Assert.Null(At(line));

    [Fact]
    public void EveryParameterHasItsStretchOfTheLabel()
    {
        foreach (var k in TjaSchema.Commands.Where(k => k.Parameters.Count > 0))
        {
            var help = TjaSignatureHelp.At(new TjaDocument(k.Name + " "), k.Name.Length + 1);
            Assert.Equal(k.Parameters.Count, help.Parameters.Count);
            for (int i = 0; i < k.Parameters.Count; i++)
                Assert.Contains(k.Parameters[i].Values.Count is > 0 and <= 4 ? k.Parameters[i].Values[0] : k.Parameters[i].Name,
                    help.Label.Substring(help.Parameters[i].Start, help.Parameters[i].Length));
        }
    }

    [Fact]
    public async Task TheServerAnswersSignatureHelp()
    {
        await using var client = LspClient.StartInProcess();
        await client.Request("initialize", LspClient.Initialize());
        const string uri = "file:///s.tja";
        await client.Notify("textDocument/didOpen", LspClient.Open(uri, "COURSE:Oni\n#START\n#SUDDEN 2 1\n#END\n"));
        var answer = await client.Request("textDocument/signatureHelp", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri }, ["position"] = new JsonObject { ["line"] = 2, ["character"] = 10 },
        });
        Assert.Equal("#SUDDEN <show> <move>", (string)answer["result"]["signatures"][0]["label"]);
        Assert.Equal(1, (int)answer["result"]["activeParameter"]);
        Assert.Equal(new[] { 15, 21 }, answer["result"]["signatures"][0]["parameters"][1]["label"].AsArray().Select(n => (int)n));
        await client.Request("shutdown");
        Assert.Equal(0, await client.Exit());
    }
}
