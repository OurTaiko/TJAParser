using System;
using System.Collections.Generic;
using System.Globalization;

namespace OurTaiko
{
    public enum TjaSeverity
    {
        // The block cannot be played as written: TjaParser.Parse throws (FormatException, or
        // NotSupportedException for a note it does not know).
        Error,
        // Parsed with OpenTaiko's tolerance: TaikoChart.Warnings.
        Warning,
        // A note for the chart's author (an editor's check, TjaLint): nothing wrong in the parse.
        Information,
    }

    // One kind of problem: a stable code (TJA1xxx errors, TJA2xxx warnings) and the English
    // message, as the exceptions and TaikoChart.Warnings word it (without the line), with {0}, {1}
    // for its variable parts.
    public sealed class TjaDiagnosticKind
    {
        public string Code { get; }
        public TjaSeverity Severity { get; }
        public string Format { get; }
        // An error that TjaParser.Parse throws as NotSupportedException rather than FormatException.
        internal bool NotSupported { get; }

        TjaDiagnosticKind(string code, TjaSeverity severity, string format, bool notSupported = false)
        {
            Code = code; Severity = severity; Format = format; NotSupported = notSupported;
            all.Add(this);
        }

        public string Message(params object[] args) => string.Format(CultureInfo.InvariantCulture, Format, args);

        public override string ToString() => Code;

        static readonly List<TjaDiagnosticKind> all = new List<TjaDiagnosticKind>();
        public static IReadOnlyList<TjaDiagnosticKind> All => all;

        static TjaDiagnosticKind Error(string code, string format, bool notSupported = false) => new TjaDiagnosticKind(code, TjaSeverity.Error, format, notSupported);
        static TjaDiagnosticKind Warning(string code, string format) => new TjaDiagnosticKind(code, TjaSeverity.Warning, format);

        // Errors.
        public static readonly TjaDiagnosticKind EmptyChart = Error("TJA1001", "The TJA chart is empty.");
        public static readonly TjaDiagnosticKind CourseNotFound = Error("TJA1002", "No {0} course (#START) on the {1} side.");
        // A number that does not read (BPM, OFFSET or LEVEL in force, #BPMCHANGE, #MEASURE): the
        // runtime's own message, such as `The input string 'x' was not in a correct format.`
        public static readonly TjaDiagnosticKind NumberUnreadable = Error("TJA1003", "{0}");
        public static readonly TjaDiagnosticKind BpmNotPositive = Error("TJA1004", "BPM must be positive and finite.");
        public static readonly TjaDiagnosticKind UnclosedBracket = Error("TJA1005", "Unclosed [ in: {0}");
        public static readonly TjaDiagnosticKind BracketWithoutNote = Error("TJA1006", "[ without a note before it in: {0}");
        public static readonly TjaDiagnosticKind MissingCommaBeforeBranch = Error("TJA1007", "A measure is missing its comma before a branch boundary.");
        public static readonly TjaDiagnosticKind UnsupportedNote = Error("TJA1008", "Unsupported TJA note: {0}", notSupported: true);
        public static readonly TjaDiagnosticKind CountOnNonBalloon = Error("TJA1009", "A balloon count on a note that is not a balloon: {0}");
        public static readonly TjaDiagnosticKind InvalidBalloonCount = Error("TJA1010", "Invalid balloon count: {0}");
        public static readonly TjaDiagnosticKind InvalidBpmChange = Error("TJA1011", "Invalid BPMCHANGE.");
        public static readonly TjaDiagnosticKind MeasureWithoutSlash = Error("TJA1012", "Invalid MEASURE (a/b): {0}");
        public static readonly TjaDiagnosticKind InvalidMeasure = Error("TJA1013", "Invalid MEASURE.");
        public static readonly TjaDiagnosticKind BranchEndWithoutStart = Error("TJA1014", "#BRANCHEND without #BRANCHSTART.");
        public static readonly TjaDiagnosticKind RouteWithoutBranch = Error("TJA1015", "Route marker without #BRANCHSTART.");
        public static readonly TjaDiagnosticKind DuplicateRoute = Error("TJA1016", "Duplicate branch route: {0}");

        // Warnings.
        public static readonly TjaDiagnosticKind EndMissing = Warning("TJA2001", "#END missing");
        // A BALLOON, BALLOONNOR, BALLOONEXP or BALLOONMAS line that does not read, as written.
        public static readonly TjaDiagnosticKind BalloonHeaderUnreadable = Warning("TJA2002", "{0}");
        public static readonly TjaDiagnosticKind LastMeasureWithoutComma = Warning("TJA2003", "Last measure without a comma");
        public static readonly TjaDiagnosticKind RollEndedByEnd = Warning("TJA2004", "An unended roll is ended by #END");
        public static readonly TjaDiagnosticKind SharedRollEndsDiffer = Warning("TJA2005", "A roll shared by the routes ends at different times; the first end is kept (measure {0})");
        public static readonly TjaDiagnosticKind BranchWithoutNormal = Warning("TJA2006", "Branch {0} has no #N: its normal route is empty");
        public static readonly TjaDiagnosticKind BranchConditionUnreadable = Warning("TJA2007", "#BRANCHSTART {0}");
        public static readonly TjaDiagnosticKind CommonNotesInBranch = Warning("TJA2008", "Notes between #BRANCHSTART and its first #N/#E/#M are common and overlap the routes (measure {0})");
        // A command the parser does not know, by its name.
        public static readonly TjaDiagnosticKind UnknownCommand = Warning("TJA2009", "{0}");
        // A command whose arguments do not read (SCROLL, DELAY, JPOSSCROLL, SUDDEN, LANE_ALPHA), dropped, as written.
        public static readonly TjaDiagnosticKind CommandDropped = Warning("TJA2010", "{0}");
        public static readonly TjaDiagnosticKind LaneAlphaBeforeDecision = Warning("TJA2011", "#LANE_ALPHA on a route starts before branch {0} is decided: {1}");
        public static readonly TjaDiagnosticKind StrayRollEnd = Warning("TJA2012", "A stray 8 with no roll to end is treated as a blank (measure {0})");
        public static readonly TjaDiagnosticKind RollHeadInRoll = Warning("TJA2013", "A roll head inside a roll is treated as a blank (measure {0})");
        public static readonly TjaDiagnosticKind RollEndedByNote = Warning("TJA2014", "An unended roll is ended by a note of type {0} (measure {1})");
        public static readonly TjaDiagnosticKind UnusedNoteE = Warning("TJA2015", "The unused note E is treated as a blank (measure {0})");
        public static readonly TjaDiagnosticKind BalloonWithoutCount = Warning("TJA2016", "A balloon has no count in {0}; it takes 5 hits (measure {1})");

        // An editor's checks (TjaLint, in TjaDocument.Diagnostics): not the parser's, so neither in
        // TaikoChart.Warnings nor in what TjaParser.Parse throws.
        static TjaDiagnosticKind Information(string code, string format) => new TjaDiagnosticKind(code, TjaSeverity.Information, format);
        // What makes SongInfo.Read refuse the whole file.
        public static readonly TjaDiagnosticKind UnsupportedCourse = Error("TJA3001", "Unsupported COURSE '{0}': the whole file is refused.");
        public static readonly TjaDiagnosticKind BlockWrittenTwice = Error("TJA3002", "{0}/{1} is written twice: the whole file is refused.");
        public static readonly TjaDiagnosticKind StartPlayer = Error("TJA3003", "#START takes P1 or P2, not '{0}': the whole file is refused.");
        public static readonly TjaDiagnosticKind LevelOutOfRange = Warning("TJA3004", "LEVEL is {0}: not 1 to 10.");
        public static readonly TjaDiagnosticKind SimilarCommand = Information("TJA3005", "Did you mean {0}?");
        public static readonly TjaDiagnosticKind UnusualSlots = Information("TJA3006", "{0} slots in a measure make notes of 1/{1}: not a division by 2s and 3s.");
        public static readonly TjaDiagnosticKind RoutesEndApart = Warning("TJA3007", "The routes of branch {0} end at different times ({1}).");

        // Whether the parser reports it (TJA1xxx, TJA2xxx); TJA3xxx are the editor's checks.
        public bool FromParser => !Code.StartsWith("TJA3", StringComparison.Ordinal);
    }

    // One problem found where it is. Every occurrence is one diagnostic (TaikoChart.Warnings keeps
    // each text once). Line counts from 1 (0: nowhere in particular, as a course not found); Span
    // is in the text (zero length at a point, as where a missing #END would be).
    public sealed class TjaDiagnostic
    {
        public TjaDiagnosticKind Kind { get; }
        public string Message { get; }
        public IReadOnlyList<object> Args { get; }
        public int Line { get; }
        public TextSpan Span { get; }

        public string Code => Kind.Code;
        public TjaSeverity Severity => Kind.Severity;

        internal TjaDiagnostic(TjaDiagnosticKind kind, string message, object[] args, int line, TextSpan span)
        {
            Kind = kind; Message = message; Args = Array.AsReadOnly(args ?? Array.Empty<object>()); Line = line; Span = span;
        }

        // The message in a language ("zh", "ja", or English for any other), for an editor.
        public string MessageIn(string language)
        {
            var args = new object[Args.Count];
            for (int i = 0; i < args.Length; i++) args[i] = Args[i];
            return TjaMessages.Format(Kind, language, args);
        }

        public override string ToString() => $"{Code} line {Line} {Span}: {Message}";
    }
}
