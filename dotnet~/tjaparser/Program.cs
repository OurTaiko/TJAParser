using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OurTaiko;

// tjaparser: the content hashes of a TJA file's blocks, for the servers.
//
//   tjaparser hash <file|->                       every block's course, side and hash, as JSON
//   tjaparser inspect <file|->                    the header metadata and every #START block in
//                                                 file order with its hash and its judgeable
//                                                 notes per route, as JSON: what the servers
//                                                 register a chart from
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
        case "inspect":
        {
            if (args.Length != 2) return Usage();
            if (!ReadText(args[1], out string text, out string error)) return Fail(3, error);
            SongInfo info;
            try { info = SongInfo.Read(text); }
            catch (FormatException e) { return Fail(4, e.Message); }
            var hashes = new Dictionary<(Difficulty, string), ChartBlockHash>();
            foreach (var h in ChartHash.OfFile(text, info)) hashes[(h.Difficulty, h.Side)] = h;
            var report = new Inspection
            {
                Version = ChartHash.Version, Prefix = ChartHash.Prefix,
                Titles = info.Titles, Subtitles = info.Subtitles, Genre = info.Genre, Maker = info.Maker,
                NotesDesigners = info.NotesDesigners,
                Bpm = info.Bpm, Offset = info.Offset, DemoStart = info.DemoStart,
                Wave = info.Wave, Movie = info.Movie, MovieOffset = info.MovieOffset,
                Blocks = info.Blocks.Select(b =>
                {
                    var entry = new InspectedBlock
                    {
                        Line = b.Line, Course = b.Course, Name = SongInfo.CourseName(b.Difficulty),
                        Side = b.Side, Player = b.Player, Style = b.Style, Level = b.Level, Branching = b.IsBranching,
                        HasNotes = b.HasNotes, Maker = b.Maker,
                    };
                    if (hashes.TryGetValue((b.Difficulty, b.Side), out var h))
                    {
                        entry.Hash = h.Hash; entry.Error = h.Error; entry.Warnings = h.Warnings.Count > 0 ? h.Warnings : null;
                        if (h.Chart != null) entry.Notes = RouteNotes(h.Chart);
                    }
                    return entry;
                }).ToList(),
            };
            Console.Out.Write(JsonSerializer.Serialize(report, ReportJson.Default.Inspection));
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

// The don and ka notes a player hits on each route, in time order: "unbranched" for a block
// without #BRANCHSTART, else "normal", "expert" and "master", each with the common notes and,
// in every branch, the route it plays (an omitted #E plays #N, an omitted #M plays #E). Times
// are RawMs (milliseconds from #START, unrounded), kinds 1–4 as written. The servers compute
// the difficulty constants' raw values from these (OurTaikoLogs/Fanmade/docs).
static Dictionary<string, NoteList> RouteNotes(TaikoChart chart)
{
    var judgeable = chart.Notes.Where(n => n.Kind >= NoteKind.Don && n.Kind <= NoteKind.BigKa)
        .Select((note, index) => (note, index)).OrderBy(x => x.note.RawMs).ThenBy(x => x.index)
        .Select(x => x.note).ToList();
    NoteList Route(BranchRoute? route)
    {
        var list = new NoteList { Ms = new List<double>(), Kinds = new List<int>() };
        foreach (var note in judgeable)
        {
            if (route != null && note.BranchId >= 0 && note.Route != chart.Branches[note.BranchId].ResolveRoute(route.Value)) continue;
            list.Ms.Add(note.RawMs);
            list.Kinds.Add((int)note.Kind);
        }
        return list;
    }
    if (chart.Branches.Count == 0) return new Dictionary<string, NoteList> { ["unbranched"] = Route(null) };
    return new Dictionary<string, NoteList>
    {
        ["normal"] = Route(BranchRoute.Normal), ["expert"] = Route(BranchRoute.Expert), ["master"] = Route(BranchRoute.Master),
    };
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
    Console.Error.WriteLine("usage: tjaparser hash <file|->\n       tjaparser inspect <file|->\n       tjaparser canonical <file|-> [course] [side]\n       tjaparser version");
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

sealed class Inspection
{
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("prefix")] public string Prefix { get; set; }
    [JsonPropertyName("titles")] public Dictionary<string, string> Titles { get; set; }
    [JsonPropertyName("subtitles")] public Dictionary<string, string> Subtitles { get; set; }
    [JsonPropertyName("genre")] public string Genre { get; set; }
    [JsonPropertyName("maker")] public string Maker { get; set; }
    [JsonPropertyName("notesDesigners")] public Dictionary<string, string> NotesDesigners { get; set; }
    [JsonPropertyName("bpm")] public double Bpm { get; set; }
    [JsonPropertyName("offset")] public double Offset { get; set; }
    [JsonPropertyName("demoStart")] public double DemoStart { get; set; }
    [JsonPropertyName("wave")] public string Wave { get; set; }
    [JsonPropertyName("movie")] public string Movie { get; set; }
    [JsonPropertyName("movieOffset")] public double MovieOffset { get; set; }
    [JsonPropertyName("blocks")] public List<InspectedBlock> Blocks { get; set; }
}

sealed class InspectedBlock
{
    [JsonPropertyName("line")] public int Line { get; set; }
    [JsonPropertyName("course")] public string Course { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("side")] public string Side { get; set; }
    [JsonPropertyName("player")] public string Player { get; set; }
    [JsonPropertyName("style")] public string Style { get; set; }
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("branching")] public bool Branching { get; set; }
    [JsonPropertyName("hasNotes")] public bool HasNotes { get; set; }
    [JsonPropertyName("maker")] public string Maker { get; set; }
    [JsonPropertyName("hash")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Hash { get; set; }
    [JsonPropertyName("error")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Error { get; set; }
    [JsonPropertyName("warnings")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string> Warnings { get; set; }
    [JsonPropertyName("notes")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, NoteList> Notes { get; set; }
}

sealed class NoteList
{
    [JsonPropertyName("ms")] public List<double> Ms { get; set; }
    [JsonPropertyName("kinds")] public List<int> Kinds { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(Report))]
[JsonSerializable(typeof(Inspection))]
partial class ReportJson : JsonSerializerContext { }
