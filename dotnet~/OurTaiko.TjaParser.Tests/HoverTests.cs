using System.Text.Json.Nodes;
using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// I2-4 (Documentation~/Parser-TODO.md): a hover tells what is at a position, as the parse has it.
public class HoverTests
{
    [Fact]
    public void ANoteIsShownAsTheParseHasIt()
    {
        int notes = 0;
        foreach (string text in new[] { Tja.Fixture("Donkama2000.tja"), Tja.Fixture("IrohaUta.tja"), Tja.Branching, Tja.Sample })
        {
            var document = new TjaDocument(text);
            var syntax = document.Syntax;
            foreach (var block in document.Blocks)
            {
                var analysis = document.Analyze(block.Course, block.Side);
                if (analysis.Chart == null) continue;
                var chart = analysis.Chart;
                for (int i = 0; i < chart.Notes.Count; i += Math.Max(1, chart.Notes.Count / 25))
                {
                    var note = chart.Notes[i];
                    analysis.SourceMap.TryGetSpan(note, out var span);
                    var hover = TjaHover.At(document, span.Start);
                    Assert.Same(note, hover.Note);
                    Assert.False(hover.Tail);
                    Assert.Equal(span, hover.Span);
                    Assert.Contains("Time " + note.Time.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " s", hover.Markdown);
                    Assert.True(hover.Measure.Span.Contains(span.Start));
                    // The slot: the note characters written before it in its measure, comments aside.
                    int slot = syntax.Count(t => t.Span.Start >= hover.Measure.Span.Start && t.Span.Start < span.Start
                        && t.Kind is TjaSyntaxKind.Note or TjaSyntaxKind.Blank or TjaSyntaxKind.RollEnd or TjaSyntaxKind.Unknown);
                    Assert.Equal(slot, hover.Slot);
                    Assert.Contains($"slot {slot + 1} of {hover.Measure.Slots}", hover.Markdown);
                    notes++;
                }
            }
        }
        Assert.True(notes > 100, notes.ToString());
    }

    [Fact]
    public void ABalloonsCountAndWhereItComesFrom()
    {
        string text = "BPM:120\nBALLOON:4,6\nBALLOONEXP:9\nCOURSE:Oni\nLEVEL:9\n#START\n7008,\n9[12]008,\n7008,\n7008,\n"
            + "#BRANCHSTART p,1,2\n#N\n1,\n#E\n7008,\n#M\n1,\n#BRANCHEND\n#END\n";
        var document = new TjaDocument(text);
        var balloons = document.Analyze().Chart.Notes.Where(n => n.IsBalloon).ToList();
        var hovers = balloons.Select(b => { document.Analyze().SourceMap.TryGetSpan(b, out var s); return TjaHover.At(document, s.Start); }).ToList();
        // A common balloon moves every route's place (as in OpenTaiko): the Expert balloon is the
        // 5th of BALLOONEXP, which has one count, so it takes 5.
        Assert.Equal(new[] { 4, 12, 5, 5, 5 }, hovers.Select(h => h.Note.BalloonHits));
        Assert.Equal(new[] { (false, "BALLOON", 0), (true, null, 0), (false, "BALLOON", 2), (false, "BALLOON", 3), (false, "BALLOONEXP", 4) },
            hovers.Select(h => (h.Inline, h.BalloonList, h.BalloonIndex)));
        Assert.Contains("4 hits, the 1st of BALLOON", hovers[0].Markdown);
        Assert.Contains("12 hits, written on it", hovers[1].Markdown);
        Assert.Contains("5 hits: BALLOON has no 3rd count", hovers[2].Markdown);
        Assert.Contains("5 hits: BALLOONEXP has no 5th count", hovers[4].Markdown);
        Assert.Contains("Branch 0, Expert route", hovers[4].Markdown);
    }

    [Fact]
    public void ARollsEndCommandsAndHeaders()
    {
        string text = "BPM:120\nBALLOON:4\nCOURSE:Oni\nLEVEL:9\n#START\n#SCROLL 1.5\n5008,\n0 // 1\n,\n#END\n";
        var document = new TjaDocument(text);
        var end = TjaHover.At(document, text.IndexOf("8,"));
        Assert.True(end.Tail);
        Assert.Equal(NoteKind.Roll, end.Note.Kind);
        Assert.Contains("End of the drumroll", end.Markdown);
        var scroll = TjaHover.At(document, text.IndexOf("1.5"));
        Assert.Equal("#SCROLL", scroll.Keyword.Name);
        Assert.Contains("#SCROLL <speed>", scroll.Markdown);
        var balloon = TjaHover.At(document, text.IndexOf("BALLOON") + 2);
        Assert.Equal("BALLOON", balloon.Keyword.Name);
        Assert.Contains("BALLOON:<hits>", balloon.Markdown);
        // A long list of values goes by the parameter's name.
        Assert.Equal("#BRANCHSTART <type>, <expert>, <master>, [l|m]", TjaHover.Signature(TjaSchema.Find("#BRANCHSTART")));
        // Nothing on a blank or in a comment.
        Assert.Null(TjaHover.At(document, text.IndexOf("0 //")));
        Assert.Null(TjaHover.At(document, text.IndexOf("// 1") + 3));
    }

    [Fact]
    public async Task TheServerAnswersHover()
    {
        await using var client = LspClient.StartInProcess();
        await client.Request("initialize", LspClient.Initialize());
        const string uri = "file:///h.tja";
        await client.Notify("textDocument/didOpen", LspClient.Open(uri, "BPM:120\nCOURSE:Oni\nLEVEL:9\n#START\n1020,\n#END\n"));
        var answer = await client.Request("textDocument/hover", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri }, ["position"] = new JsonObject { ["line"] = 4, ["character"] = 2 },
        });
        Assert.StartsWith("**Ka** カッ", (string)answer["result"]["contents"]["value"]);
        Assert.Equal(2, (int)answer["result"]["range"]["start"]["character"]);
        await client.Request("shutdown");
        Assert.Equal(0, await client.Exit());
    }
}
