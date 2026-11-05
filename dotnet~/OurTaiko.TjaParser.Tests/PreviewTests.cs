using System.Text.Json.Nodes;
using OurTaiko;
using OurTaiko.Corpus;
using Xunit;

namespace OurTaiko.Tests;

// I2-7 (Documentation~/Parser-TODO.md): a preview and the text find each other. From a note's
// place in the text to its time (the cursor, StateAt) and back to a note (a click, NoteNear): the
// same note.
public class PreviewTests
{
    [Fact]
    public void FromTheTextToTheTimeAndBackIsTheSameNote()
    {
        int notes = 0;
        var texts = new[] { Tja.Fixture("Donkama2000.tja"), Tja.Fixture("IrohaUta.tja"), Tja.Branching }
            .Concat(Enumerable.Range(0, 20).Select(i => ChartCorpus.Generate(19000 + i, 12, messy: false)));
        foreach (string text in texts)
        {
            var document = new TjaDocument(text);
            foreach (var block in document.Blocks)
            {
                var analysis = document.Analyze(block.Course, block.Side);
                if (analysis.Chart == null) continue;
                var chart = analysis.Chart;
                for (int i = 0; i < chart.Notes.Count; i += Math.Max(1, chart.Notes.Count / 20))
                {
                    var note = chart.Notes[i];
                    // Two notes at one time on one route are one place; the first is found.
                    var route = note.BranchId >= 0 ? note.Route : BranchRoute.Normal;
                    var same = chart.Notes.Where(n => n.Time == note.Time && (n.BranchId < 0 || n.Route == chart.Branches[n.BranchId].ResolveRoute(route))).ToList();
                    if (same.Count > 1) continue;
                    analysis.SourceMap.TryGetSpan(note, out var span);
                    var state = analysis.StateAt(span.Start);
                    Assert.Equal(note.Time, state.Time);
                    Assert.Same(note, analysis.NoteNear(state.Time, state.Route ?? BranchRoute.Normal));
                    notes++;
                }
            }
        }
        Assert.True(notes > 300, notes.ToString());
    }

    [Fact]
    public void AClickBetweenNotesFindsTheNearestOnTheRoute()
    {
        var a = TjaParser.Analyze("BPM:120\nCOURSE:Oni\n#START\n1010,\n#BRANCHSTART p,1,2\n#N\n1,\n#E\n02,\n#M\n0003,\n#BRANCHEND\n#END\n");
        Assert.Equal(1.0, a.NoteNear(1.2).Time);
        Assert.Equal(NoteKind.Don, a.NoteNear(2.2, BranchRoute.Normal).Kind);
        Assert.Equal(NoteKind.Ka, a.NoteNear(2.2, BranchRoute.Expert).Kind);
        Assert.Equal(NoteKind.BigDon, a.NoteNear(9, BranchRoute.Master).Kind);
    }

    [Fact]
    public async Task TheServerTellsTheTimeAtTheCursorAndTheNoteAtATime()
    {
        await using var client = LspClient.StartInProcess();
        await client.Request("initialize", LspClient.Initialize());
        const string uri = "file:///p.tja";
        await client.Notify("textDocument/didOpen", LspClient.Open(uri, "BPM:120\nCOURSE:Oni\nLEVEL:9\n#START\n1020,\n3,\n#END\n"));
        var time = await client.Request("tja/timeAt", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri }, ["position"] = new JsonObject { ["line"] = 4, ["character"] = 2 },
        });
        Assert.Equal(1.0, (double)time["result"]["time"]);
        Assert.Equal("Oni", (string)time["result"]["course"]);
        var note = await client.Request("tja/noteAt", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri }, ["course"] = "Oni", ["side"] = "single", ["time"] = 1.9,
        });
        Assert.Equal("BigDon", (string)note["result"]["kind"]);
        Assert.Equal((5, 0), ((int)note["result"]["range"]["start"]["line"], (int)note["result"]["range"]["start"]["character"]));
        await client.Request("shutdown");
        Assert.Equal(0, await client.Exit());
    }
}
