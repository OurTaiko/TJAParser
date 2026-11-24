using System.Reflection;
using Xunit;

namespace OurTaiko.Tests;

// The tjaparser command as the servers run it: its exit codes.
// The console is the process's: tests that capture it run one after another.
[Collection("Console")]
public class CliTests
{
    // The command is self-contained, so it runs here through its entry point; only these tests
    // write to the console, and the tests of one class run one at a time.
    internal static (int Code, string Out, string Err) Run(string text, params string[] args)
    {
        string file = Path.GetTempFileName();
        var (stdout, stderr) = (Console.Out, Console.Error);
        try
        {
            File.WriteAllText(file, text);
            var output = new StringWriter();
            var error = new StringWriter();
            Console.SetOut(output);
            Console.SetError(error);
            var main = Assembly.Load("tjaparser").EntryPoint;
            int code = (int)main.Invoke(null, new object[] { args.Append(file).ToArray() });
            return (code, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("inspect")]
    public void ARefusedFileIsExitFour(string command)
    {
        var (code, output, error) = Run("COURSE:Tower\n#START\n1,\n#END\n", command);
        Assert.Equal(4, code);
        Assert.Equal("", output);
        Assert.Contains("Unsupported COURSE 'Tower'", error);
    }

    // The servers read a chart's headers from inspect, the play backdrop it asks for among them.
    [Fact]
    public void InspectSaysTheScenePreset()
    {
        var (code, output, _) = Run("SCENEPRESET: MIKU \n" + Tja.Sample, "inspect");
        Assert.Equal(0, code);
        Assert.Contains("\"scenePreset\":\"MIKU\"", output);
        (code, output, _) = Run(Tja.Sample, "inspect");
        Assert.Equal(0, code);
        Assert.Contains("\"scenePreset\":\"\"", output);
    }

    // The servers score uploaded plays from each block's ShinuchiScoring; a block that does not
    // parse has none.
    [Fact]
    public void InspectGivesEachBlocksScoring()
    {
        var (code, output, _) = Run("BPM:120\nCOURSE:Oni\n#START\n1110,\n#END\nCOURSE:Hard\n#START\n1H,\n#END\n", "inspect");
        Assert.Equal(0, code);
        var blocks = System.Text.Json.Nodes.JsonNode.Parse(output)!["blocks"]!.AsArray();
        var oni = blocks.Single(b => (string)b!["name"]! == "Oni")!["scoring"]!;
        Assert.Equal(333340, (int)oni["goodPoints"]!);
        Assert.Equal(166670, (int)oni["okPoints"]!);
        Assert.Equal(100, (int)oni["longHitPoints"]!);
        Assert.Equal(1000020, (int)oni["kiwamiThreshold"]!);
        Assert.Null(blocks.Single(b => (string)b!["name"]! == "Hard")!["scoring"]);
    }

    [Fact]
    public void HashListsTheBlocks()
    {
        var (code, output, _) = Run(Tja.Sample, "hash");
        Assert.Equal(0, code);
        Assert.StartsWith("{\"version\":2,\"prefix\":\"v2:\",\"blocks\":[", output);
    }
}
