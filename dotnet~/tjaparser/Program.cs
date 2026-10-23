using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OurTaiko;

// tjaparser: the content hashes of a TJA file's blocks, for the servers.
//
//   tjaparser hash <file|->                       every block's course, side and hash, as JSON
//   tjaparser canonical <file|-> [course] [side]  the canonical text one block is hashed over
//   tjaparser version                             the hash algorithm version
//
// The file must be UTF-8 (a BOM is fine); the servers hand over their normalised copy.
// Exit codes: 0 done (blocks that failed to parse carry an error in the JSON), 2 usage,
// 3 the input could not be read, 4 the block asked for is not there or did not parse.
return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0) return Usage();
    switch (args[0])
    {
        case "version":
            Console.WriteLine(ChartHash.Prefix.TrimEnd(':'));
            return 0;
        case "hash":
        {
            if (args.Length != 2) return Usage();
            if (!ReadText(args[1], out string text, out string error)) return Fail(3, error);
            var blocks = ChartHash.OfFile(text).Select(b => new Block
            {
                Course = b.Course, Side = b.Side, Level = b.Level, Branching = b.IsBranching, Hash = b.Hash, Error = b.Error,
            }).ToList();
            var report = new Report { Version = ChartHash.Version, Prefix = ChartHash.Prefix, Blocks = blocks };
            Console.Out.Write(JsonSerializer.Serialize(report, ReportJson.Default.Report));
            Console.Out.Write('\n');
            return 0;
        }
        case "canonical":
        {
            if (args.Length < 2 || args.Length > 4) return Usage();
            if (!ReadText(args[1], out string text, out string error)) return Fail(3, error);
            string course = args.Length > 2 ? args[2] : "Oni", side = args.Length > 3 ? args[3] : "single";
            try
            {
                Console.Out.Write(ChartCanonical.Write(TjaParser.Parse(text, course, side)));
                return 0;
            }
            catch (Exception e) { return Fail(4, e.Message); }
        }
        default:
            return Usage();
    }
}

static bool ReadText(string path, out string text, out string error)
{
    text = null; error = null;
    try
    {
        byte[] bytes;
        if (path == "-")
        {
            using var stdin = Console.OpenStandardInput();
            using var buffer = new MemoryStream();
            stdin.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        else bytes = File.ReadAllBytes(path);
        text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        return true;
    }
    catch (DecoderFallbackException) { error = "The input is not valid UTF-8."; }
    catch (Exception e) { error = e.Message; }
    return false;
}

static int Fail(int code, string message)
{
    Console.Error.WriteLine("tjaparser: " + message);
    return code;
}

static int Usage()
{
    Console.Error.WriteLine("usage: tjaparser hash <file|->\n       tjaparser canonical <file|-> [course] [side]\n       tjaparser version");
    return 2;
}

sealed class Report
{
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("prefix")] public string Prefix { get; set; }
    [JsonPropertyName("blocks")] public List<Block> Blocks { get; set; }
}

sealed class Block
{
    [JsonPropertyName("course")] public string Course { get; set; }
    [JsonPropertyName("side")] public string Side { get; set; }
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("branching")] public bool Branching { get; set; }
    [JsonPropertyName("hash")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Hash { get; set; }
    [JsonPropertyName("error")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Error { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(Report))]
partial class ReportJson : JsonSerializerContext { }
