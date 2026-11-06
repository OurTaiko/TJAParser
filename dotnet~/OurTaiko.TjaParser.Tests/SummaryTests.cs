using System.Text.Json.Nodes;
using OurTaiko;
using OurTaiko.Corpus;
using Xunit;

namespace OurTaiko.Tests;

// I2-8 (Documentation~/Parser-TODO.md): a block's summary says what the CLI's inspect says of it,
// and hints mark each measure where it starts.
[Collection("Console")]
public class SummaryTests
{
    [Fact]
    public void ABlocksSummaryIsWhatInspectSays()
    {
        int blocks = 0;
        var texts = new[] { Tja.Fixture("Donkama2000.tja"), Tja.Fixture("IrohaUta.tja"), Tja.Branching, Tja.Sample }
            .Concat(Enumerable.Range(0, 20).Select(i => ChartCorpus.Generate(21000 + i, 12, messy: false)));
        foreach (string text in texts)
        {
            var (code, output, _) = CliTests.Run(text, "inspect");
            Assert.Equal(0, code);
            var inspected = JsonNode.Parse(output)["blocks"].AsArray()
                .ToDictionary(b => ((string)b["name"], (string)b["side"]));
            foreach (var summary in TjaSummary.Blocks(new TjaDocument(text)))
            {
                var block = inspected[(SongInfo.CourseName(SongInfo.DifficultyOf(summary.Block.Course).Value), summary.Block.Side)];
                Assert.Equal((string)block["hash"], summary.Hash);
                var notes = block["notes"].AsObject();
                Assert.Equal(notes.Select(n => n.Key).OrderBy(k => k), summary.Notes.Keys.OrderBy(k => k));
                foreach (var (route, list) in notes) Assert.Equal(list["kinds"].AsArray().Count, summary.Notes[route]);
                blocks++;
            }
        }
        Assert.True(blocks > 30, blocks.ToString());
    }

    const string Text = "BPM:120\nCOURSE:Oni\nLEVEL:9\n#START\n11111111,\n#BPMCHANGE 240\n3040,\nG,\n#END\n";

    [Fact]
    public void TheSummaryCountsNotesComboAndThePeak()
    {
        var summary = TjaSummary.Blocks(new TjaDocument(Text)).Single();
        // 8 eighths in two seconds, then 3 and 4 at 240 BPM, then a Kadon (combo, not a don or ka).
        Assert.Equal(10, summary.Notes["unbranched"]);
        Assert.Equal(11, summary.Combo["unbranched"]);
        Assert.Equal(4, summary.PeakPerSecond["unbranched"]);
        Assert.Equal(4, summary.Duration);
        Assert.StartsWith("10 notes · max combo 11 · 4 s · peak 4/s · v2:", summary.Title);
        var branched = TjaSummary.Blocks(new TjaDocument(Tja.Branching)).Single();
        // 1111, then N 1000, E 1010 or M 1111, then 2222.
        Assert.StartsWith("9/10/12 notes", branched.Title);
    }

    [Fact]
    public void HintsMarkEachMeasureAndTheBpmWhereItChanges()
    {
        var document = new TjaDocument(Text);
        var hints = TjaSummary.Hints(document, new TextSpan(0, Text.Length));
        Assert.Equal(new[] { "#1 0.000s ♩120", "#2 2.000s ♩240", "#3 3.000s" }, hints.Select(h => h.Label));
        Assert.Equal(new[] { Text.IndexOf("11111111"), Text.IndexOf("3040"), Text.IndexOf("G,") }, hints.Select(h => h.Offset));
        // Only those in the stretch asked for.
        Assert.Equal(new[] { "#2 2.000s ♩240" }, TjaSummary.Hints(document, new TextSpan(Text.IndexOf("3040"), 4)).Select(h => h.Label));
    }

    [Fact]
    public async Task TheServerAnswersInlayHintsAndCodeLens()
    {
        await using var client = LspClient.StartInProcess();
        await client.Request("initialize", LspClient.Initialize());
        const string uri = "file:///i.tja";
        await client.Notify("textDocument/didOpen", LspClient.Open(uri, Text));
        var hints = await client.Request("textDocument/inlayHint", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["range"] = new JsonObject { ["start"] = new JsonObject { ["line"] = 0, ["character"] = 0 }, ["end"] = new JsonObject { ["line"] = 9, ["character"] = 0 } },
        });
        Assert.Equal(3, hints["result"].AsArray().Count);
        Assert.Equal(6, (int)hints["result"][1]["position"]["line"]);
        var lenses = await client.Request("textDocument/codeLens", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } });
        Assert.Equal(3, (int)lenses["result"][0]["range"]["start"]["line"]);
        Assert.StartsWith("10 notes", (string)lenses["result"][0]["command"]["title"]);
        await client.Request("shutdown");
        Assert.Equal(0, await client.Exit());
    }
}
