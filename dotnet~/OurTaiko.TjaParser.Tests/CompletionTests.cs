using System.Text.Json.Nodes;
using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// I2-3 (Documentation~/Parser-TODO.md): completion by where the position is. `|` marks it.
public class CompletionTests
{
    const string Head = "TITLE:Song\nBPM:120\n";
    const string Body = "COURSE:Oni\nLEVEL:9\n#START\n1010,\n";

    static List<TjaCompletionItem> At(string marked, out string text)
    {
        int at = marked.IndexOf('|');
        text = marked.Remove(at, 1);
        return TjaCompletion.At(new TjaDocument(text), at);
    }

    static List<string> Labels(string marked) => At(marked, out _).Select(i => i.Label).ToList();

    [Fact]
    public void OutsideABlockHeadersAndTheirValues()
    {
        var all = Labels(Head + "|\n" + Body + "#END\n");
        Assert.Contains("COURSE", all);
        Assert.Contains("BALLOONMAS", all);
        Assert.DoesNotContain("#BPMCHANGE", all);
        Assert.Equal(new[] { "COURSE" }, Labels(Head + "CO|\n" + Body + "#END\n"));
        Assert.Equal(new[] { "Easy", "Normal", "Hard", "Oni", "Edit", "Ura", "0", "1", "2", "3", "4" }, Labels(Head + "COURSE:|\n#START\n1,\n#END\n"));
        Assert.Equal(new[] { "Oni" }, Labels(Head + "COURSE: o|\n#START\n1,\n#END\n"));
        Assert.Equal(new[] { "Single", "Double" }, Labels(Head + "STYLE:|\n"));
        Assert.Equal(Enumerable.Range(1, 10).Select(i => i.ToString()), Labels(Head + "LEVEL:|\n"));
        Assert.Empty(Labels(Head + "TITLE:|\n"));
        // Commands outside a block: #START and the scroll modes.
        Assert.Equal(new[] { "#START", "#NMSCROLL", "#BMSCROLL", "#HBSCROLL" }, Labels(Head + "COURSE:Oni\n#|\n"));
        Assert.Equal(new[] { "P1", "P2" }, Labels(Head + "COURSE:Oni\n#START |\n1,\n#END\n"));
    }

    [Fact]
    public void InABlockCommandsByWhereTheyAre()
    {
        var plain = Labels(Head + Body + "#|\n1,\n#END\n");
        Assert.Contains("#BPMCHANGE", plain);
        Assert.Contains("#END", plain);
        Assert.Contains("#LANE_ALPHA", plain);
        Assert.DoesNotContain("#START", plain);
        // Route markers and #BRANCHEND only in a branch.
        Assert.DoesNotContain("#N", plain);
        Assert.DoesNotContain("#BRANCHEND", plain);
        var branch = Labels(Head + Body + "#BRANCHSTART p,50,80\n#N\n1,\n#|\n1,\n#BRANCHEND\n#END\n");
        Assert.Contains("#E", branch);
        Assert.Contains("#BRANCHEND", branch);
        Assert.Equal(new[] { "#GOGOSTART" }, Labels(Head + Body + "#gogos|\n1,\n#END\n"));
        Assert.Equal(new[] { "p", "pp", "jp", "d", "jg", "jb", "r", "rb", "s" }, Labels(Head + Body + "#BRANCHSTART |\n#N\n1,\n#BRANCHEND\n#END\n"));
        Assert.Equal(new[] { "r", "rb" }, Labels(Head + Body + "#BRANCHSTART r|\n#N\n1,\n#BRANCHEND\n#END\n"));
        Assert.Empty(Labels(Head + Body + "#BRANCHSTART p,|\n#N\n1,\n#BRANCHEND\n#END\n"));
    }

    [Fact]
    public void InANoteLineTheNotes()
    {
        var notes = At(Head + Body + "10|\n#END\n", out _);
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6", "7", "9", "8", "0", "G", "C", "F", "," }, notes.Select(i => i.Label));
        Assert.All(notes, i => Assert.Equal(TjaCompletionKind.Note, i.Kind));
        Assert.Equal("Don", notes[0].Detail);
        // A line not begun: the notes and the commands.
        var empty = Labels(Head + Body + "|\n#END\n");
        Assert.Contains("1", empty);
        Assert.Contains("#GOGOSTART", empty);
        // Nothing in a comment.
        Assert.Empty(Labels(Head + Body + "10 // #|\n#END\n"));
        Assert.Empty(Labels(Head + "/* CO|\n*/" + Body + "#END\n"));
    }

    [Fact]
    public void AnItemReplacesWhatIsTyped()
    {
        var item = At(Head + "COURSE: o|\n#START\n1,\n#END\n", out string text).Single();
        string done = text.Substring(0, item.Replace.Start) + item.Insert + text.Substring(item.Replace.End);
        Assert.StartsWith(Head + "COURSE: Oni\n", done);
        var header = At(Head + "BAL|\n", out text).First(i => i.Label == "BALLOON");
        Assert.Equal(Head + "BALLOON:\n", text.Substring(0, header.Replace.Start) + header.Insert + text.Substring(header.Replace.End));
    }

    [Fact]
    public async Task TheServerAnswersCompletion()
    {
        await using var client = LspClient.StartInProcess();
        await client.Request("initialize", LspClient.Initialize());
        const string uri = "file:///c.tja";
        await client.Notify("textDocument/didOpen", LspClient.Open(uri, "COURSE: o\n"));
        var answer = await client.Request("textDocument/completion", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri }, ["position"] = new JsonObject { ["line"] = 0, ["character"] = 9 },
        });
        var item = answer["result"]["items"].AsArray().Single();
        Assert.Equal("Oni", (string)item["label"]);
        Assert.Equal(20, (int)item["kind"]);
        Assert.Equal(8, (int)item["textEdit"]["range"]["start"]["character"]);
        Assert.Equal(9, (int)item["textEdit"]["range"]["end"]["character"]);
        await client.Request("shutdown");
        Assert.Equal(0, await client.Exit());
    }
}
