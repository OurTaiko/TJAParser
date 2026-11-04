using System;
using System.Collections.Generic;

namespace OurTaiko
{
    // Where a command or header may be written.
    [Flags]
    public enum TjaScope
    {
        // A KEY:VALUE line outside a block.
        Header = 1,
        // A command line outside a block (#START, or a scroll mode before a course's #START).
        Outside = 2,
        // A line inside #START … #END.
        Body = 4,
    }

    // Where a command or header comes from.
    public enum TjaOrigin
    {
        // TaikoJiro's TJA.
        Tja,
        // OpenTaiko (and the TJAPlayer line before it).
        OpenTaiko,
        // This library (not in OpenTaiko).
        OurTaiko,
    }

    public enum TjaValueType
    {
        // A number as TjaNumber reads it (a comma is a decimal point).
        Number,
        // A whole number.
        Integer,
        // `a/b`.
        Ratio,
        // A real or, with i, a complex number (SCROLL).
        Complex,
        // One of Values.
        Choice,
        // Comma-separated whole numbers.
        IntegerList,
        Text,
    }

    // What becomes of a value that does not read.
    public enum TjaOnError
    {
        // Nothing is read from it (no parameter, or text taken as written).
        None,
        // The block fails (TjaParser.Parse throws).
        FailsBlock,
        // Dropped with a warning; what was in force stays.
        Dropped,
        // Taken as a default (SongInfo), or as SongInfo's, see the specification.
        Default,
    }

    public sealed class TjaParameter
    {
        public string Name { get; }
        public TjaValueType Type { get; }
        public bool Optional { get; }
        public string Unit { get; }
        public IReadOnlyList<string> Values { get; }

        internal TjaParameter(string name, TjaValueType type, bool optional = false, string unit = null, params string[] values)
        {
            Name = name; Type = type; Optional = optional; Unit = unit; Values = values ?? Array.Empty<string>();
        }
    }

    // One command (#NAME) or header (NAME:) of a TJA file. Section is where the specification
    // (Documentation~/TJA-Specification.md) describes it; Summary is a line for an editor's hover.
    public sealed class TjaKeyword
    {
        public string Name { get; }
        public TjaScope Scope { get; }
        public IReadOnlyList<TjaParameter> Parameters { get; }
        public TjaOrigin Origin { get; }
        // Whether it can change what is played, and so the content hash.
        public bool InHash { get; }
        public TjaOnError OnError { get; }
        public string Section { get; }
        public string Summary { get; }

        internal TjaKeyword(string name, TjaScope scope, TjaOrigin origin, bool inHash, TjaOnError onError, string section, string summary, params TjaParameter[] parameters)
        {
            Name = name; Scope = scope; Origin = origin; InHash = inHash; OnError = onError; Section = section; Summary = summary;
            Parameters = parameters ?? Array.Empty<TjaParameter>();
        }

        public bool IsCommand => Name[0] == '#';
    }

    // Every command and header the library reads: one table for the parser's dispatch, an
    // editor's completion and hover, and the specification's command list.
    public static class TjaSchema
    {
        static TjaParameter P(string name, TjaValueType type, string unit = null) => new TjaParameter(name, type, unit: unit);
        static TjaParameter Opt(string name, TjaValueType type, string unit = null) => new TjaParameter(name, type, optional: true, unit: unit);

        const TjaScope Body = TjaScope.Body, Header = TjaScope.Header;

        public static readonly IReadOnlyList<TjaKeyword> Commands = new[]
        {
            new TjaKeyword("#START", TjaScope.Outside, TjaOrigin.Tja, false, TjaOnError.None, "3.2", "Starts a chart block; P1 or P2 for a side of a double chart.",
                new TjaParameter("player", TjaValueType.Choice, optional: true, values: new[] { "P1", "P2" })),
            new TjaKeyword("#END", Body, TjaOrigin.Tja, false, TjaOnError.None, "1.5", "Ends the chart block."),
            new TjaKeyword("#BPMCHANGE", Body, TjaOrigin.Tja, true, TjaOnError.FailsBlock, "6.1", "Changes the BPM from here.", P("bpm", TjaValueType.Number, "BPM")),
            new TjaKeyword("#MEASURE", Body, TjaOrigin.Tja, true, TjaOnError.FailsBlock, "6.2", "Changes the time signature from the next slot.", P("a/b", TjaValueType.Ratio)),
            new TjaKeyword("#SCROLL", Body, TjaOrigin.Tja, true, TjaOnError.Dropped, "6.3", "Changes the scroll speed; with i, a complex speed whose imaginary part is vertical.", P("speed", TjaValueType.Complex)),
            new TjaKeyword("#DELAY", Body, TjaOrigin.Tja, true, TjaOnError.Dropped, "6.4", "Moves the cursor on by seconds without moving the beat.", P("seconds", TjaValueType.Number, "s")),
            new TjaKeyword("#GOGOSTART", Body, TjaOrigin.Tja, true, TjaOnError.None, "6.5", "Starts GOGO time here."),
            new TjaKeyword("#GOGOEND", Body, TjaOrigin.Tja, true, TjaOnError.None, "6.5", "Ends GOGO time here."),
            new TjaKeyword("#BARLINEOFF", Body, TjaOrigin.Tja, true, TjaOnError.None, "6.6", "Hides the bar lines from here."),
            new TjaKeyword("#BARLINEON", Body, TjaOrigin.Tja, true, TjaOnError.None, "6.6", "Shows the bar lines from here."),
            new TjaKeyword("#SECTION", Body, TjaOrigin.Tja, true, TjaOnError.None, "6.7", "Resets the branch counts here."),
            new TjaKeyword("#BRANCHSTART", Body, TjaOrigin.Tja, true, TjaOnError.Dropped, "8.2", "Starts a branch: its condition and the thresholds for Expert and Master.",
                new TjaParameter("type", TjaValueType.Choice, values: new[] { "p", "pp", "jp", "d", "jg", "jb", "r", "rb", "s" }),
                P("expert", TjaValueType.Number), P("master", TjaValueType.Number),
                new TjaParameter("range", TjaValueType.Choice, optional: true, values: new[] { "l", "m" })),
            new TjaKeyword("#N", Body, TjaOrigin.Tja, true, TjaOnError.FailsBlock, "8.1", "The Normal route of the branch."),
            new TjaKeyword("#E", Body, TjaOrigin.Tja, true, TjaOnError.FailsBlock, "8.1", "The Expert route of the branch."),
            new TjaKeyword("#M", Body, TjaOrigin.Tja, true, TjaOnError.FailsBlock, "8.1", "The Master route of the branch."),
            new TjaKeyword("#BRANCHEND", Body, TjaOrigin.Tja, true, TjaOnError.FailsBlock, "8.1", "Ends the branch: common notes again."),
            new TjaKeyword("#LEVELHOLD", Body, TjaOrigin.Tja, true, TjaOnError.None, "6.9", "Keeps the route: in a route, once it is chosen; elsewhere, the route being played."),
            new TjaKeyword("#NMSCROLL", TjaScope.Outside | Body, TjaOrigin.OpenTaiko, true, TjaOnError.None, "6.10", "Notes move by their own time, BPM and SCROLL."),
            new TjaKeyword("#BMSCROLL", TjaScope.Outside | Body, TjaOrigin.OpenTaiko, true, TjaOnError.None, "6.10", "Notes move by the shared beat clock, SCROLL ignored."),
            new TjaKeyword("#HBSCROLL", TjaScope.Outside | Body, TjaOrigin.OpenTaiko, true, TjaOnError.None, "6.10", "Notes move by the shared beat clock, times SCROLL."),
            new TjaKeyword("#JPOSSCROLL", Body, TjaOrigin.OpenTaiko, true, TjaOnError.Dropped, "6.11", "Moves the judge by move pixels (1280 wide) over seconds; direction 0 turns it around.",
                P("seconds", TjaValueType.Number, "s"), P("move", TjaValueType.Complex, "px"), Opt("direction", TjaValueType.Integer)),
            new TjaKeyword("#SUDDEN", Body, TjaOrigin.OpenTaiko, true, TjaOnError.Dropped, "6.12", "Notes appear show seconds and start to move move seconds before they are hit.",
                P("show", TjaValueType.Number, "s"), P("move", TjaValueType.Number, "s")),
            new TjaKeyword("#LANE_ALPHA", Body, TjaOrigin.OurTaiko, false, TjaOnError.Dropped, "6.13", "Fades the lane background to target percent opacity, over ms (negative: up to here).",
                P("target", TjaValueType.Number, "%"), Opt("ms", TjaValueType.Number, "ms")),
        };

        static readonly string[] Locales = { "EN", "JA", "JP", "KO", "ZH", "CN", "ZH_CN", "ZH-CN", "TW", "ZH_TW", "ZH-TW" };

        public static readonly IReadOnlyList<TjaKeyword> Headers = HeaderList();

        static TjaKeyword[] HeaderList()
        {
            var list = new List<TjaKeyword>
            {
                new TjaKeyword("TITLE", Header, TjaOrigin.Tja, false, TjaOnError.None, "2.2", "The song's title.", P("title", TjaValueType.Text)),
                new TjaKeyword("SUBTITLE", Header, TjaOrigin.Tja, false, TjaOnError.None, "2.2", "The subtitle; -- hides it in song select, ++ shows it.", P("subtitle", TjaValueType.Text)),
                new TjaKeyword("BPM", Header, TjaOrigin.Tja, true, TjaOnError.FailsBlock, "2.2", "The BPM at the start.", P("bpm", TjaValueType.Number, "BPM")),
                new TjaKeyword("OFFSET", Header, TjaOrigin.Tja, true, TjaOnError.FailsBlock, "2.2", "Seconds from the audio's start to the chart's (negative: later).", P("seconds", TjaValueType.Number, "s")),
                new TjaKeyword("COURSE", Header, TjaOrigin.Tja, false, TjaOnError.None, "3.1", "The difficulty of the blocks that follow.",
                    new TjaParameter("course", TjaValueType.Choice, values: new[] { "Easy", "Normal", "Hard", "Oni", "Edit", "Ura", "0", "1", "2", "3", "4" })),
                new TjaKeyword("LEVEL", Header, TjaOrigin.Tja, false, TjaOnError.FailsBlock, "2.2", "The course's stars.", P("stars", TjaValueType.Integer)),
                new TjaKeyword("BALLOON", Header, TjaOrigin.Tja, true, TjaOnError.Dropped, "7.1", "Hits of each balloon in turn (the Normal route's list).", P("hits", TjaValueType.IntegerList)),
                new TjaKeyword("BALLOONNOR", Header, TjaOrigin.OpenTaiko, true, TjaOnError.Dropped, "7.1", "Hits of each balloon on the Normal route.", P("hits", TjaValueType.IntegerList)),
                new TjaKeyword("BALLOONEXP", Header, TjaOrigin.OpenTaiko, true, TjaOnError.Dropped, "7.1", "Hits of each balloon on the Expert route.", P("hits", TjaValueType.IntegerList)),
                new TjaKeyword("BALLOONMAS", Header, TjaOrigin.OpenTaiko, true, TjaOnError.Dropped, "7.1", "Hits of each balloon on the Master route.", P("hits", TjaValueType.IntegerList)),
                new TjaKeyword("STYLE", Header, TjaOrigin.Tja, false, TjaOnError.None, "2.2", "Single or Double: whether the course is written for two players.",
                    new TjaParameter("style", TjaValueType.Choice, values: new[] { "Single", "Double" })),
                new TjaKeyword("GENRE", Header, TjaOrigin.Tja, false, TjaOnError.None, "2.2", "The song's genre.", P("genre", TjaValueType.Text)),
                new TjaKeyword("WAVE", Header, TjaOrigin.Tja, false, TjaOnError.None, "2.2", "The audio file.", P("file", TjaValueType.Text)),
                new TjaKeyword("BGMOVIE", Header, TjaOrigin.Tja, false, TjaOnError.None, "2.2", "The background movie file.", P("file", TjaValueType.Text)),
                new TjaKeyword("MOVIEOFFSET", Header, TjaOrigin.Tja, false, TjaOnError.Default, "2.2", "Seconds the movie starts after the audio.", P("seconds", TjaValueType.Number, "s")),
                new TjaKeyword("DEMOSTART", Header, TjaOrigin.Tja, false, TjaOnError.Default, "2.2", "Where song select's preview starts.", P("seconds", TjaValueType.Number, "s")),
                new TjaKeyword("MAKER", Header, TjaOrigin.OpenTaiko, false, TjaOnError.None, "2.2", "The chart's author.", P("name", TjaValueType.Text)),
            };
            for (int i = 0; i <= 4; i++)
                list.Add(new TjaKeyword("NOTESDESIGNER" + i, Header, TjaOrigin.OpenTaiko, false, TjaOnError.None, "2.2",
                    $"The author of the {SongInfo.CourseNames[i]} course.", P("name", TjaValueType.Text)));
            foreach (string locale in Locales)
            {
                list.Add(new TjaKeyword("TITLE" + locale, Header, TjaOrigin.OpenTaiko, false, TjaOnError.None, "2.2", "The title in a language.", P("title", TjaValueType.Text)));
                list.Add(new TjaKeyword("SUBTITLE" + locale, Header, TjaOrigin.OpenTaiko, false, TjaOnError.None, "2.2", "The subtitle in a language.", P("subtitle", TjaValueType.Text)));
            }
            return list.ToArray();
        }

        static readonly Dictionary<string, TjaKeyword> byName = Index();

        static Dictionary<string, TjaKeyword> Index()
        {
            var index = new Dictionary<string, TjaKeyword>(StringComparer.Ordinal);
            foreach (var k in Commands) index.Add(k.Name, k);
            foreach (var k in Headers) index.Add(k.Name, k);
            return index;
        }

        // A command (#NAME, any case) or header key (NAME, any case), or null.
        public static TjaKeyword Find(string name) => name != null && byName.TryGetValue(name.ToUpperInvariant(), out var k) ? k : null;
    }
}
