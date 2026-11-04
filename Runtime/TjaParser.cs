using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace OurTaiko
{
    // TJA time is measured relative to the audio start. Positive OFFSET starts the chart earlier.
    // Commands are kept between note slots, so a BPM/SCROLL change inside a measure stays in place.
    public static class TjaParser
    {
        static double Number(string value) => TjaNumber.Parse(value);
        // A numeric COURSE (0–4) by its name; any other value as written.
        static string Course(string value)
            => int.TryParse(value, out int n) && n >= 0 && n < SongInfo.CourseNames.Count ? SongInfo.CourseNames[n] : value;

        // By difficulty, as SongInfo reads COURSE:, so `Edit`, `Ura` and `4` are one course; a value
        // SongInfo does not know is compared as written.
        static bool SameCourse(string course, string requestedCourse)
        {
            var difficulty = course == null ? null : SongInfo.DifficultyOf(course);
            var requested = requestedCourse == null ? null : SongInfo.DifficultyOf(requestedCourse);
            if (difficulty != null || requested != null) return difficulty == requested;
            return string.Equals(Course(course), Course(requestedCourse), StringComparison.OrdinalIgnoreCase);
        }

        // One parse for every mode: how branches are played is PlaySession's choice. The block of
        // the requested course on the requested side ("single", "p1" or "p2", #START's player) is
        // played; which side a seat plays is the caller's choice (SongInfo.Block,
        // FanmadeChart.Block), so a single block stays playable where the course also has a P1 one.
        public static TaikoChart Parse(string text, string requestedCourse = "Oni", string requestedSide = "single")
            => Parse(text, TjaLines.Read(text), requestedCourse, requestedSide);

        // The same from the file's lines already read (TjaLines.Read of text), so that several
        // blocks of one file are parsed without reading it again (ChartHash.OfFile).
        internal static TaikoChart Parse(string text, IEnumerable<TjaLine> lines, string requestedCourse, string requestedSide)
            => new ChartParser(null).Run(text, lines, requestedCourse, requestedSide);

        // One block with every problem where it is (TjaAnalysis), for an editor. Unlike Parse it
        // does not stop at an error: it notes it, reads on as the error's recovery says, and gives
        // the chart that comes out. Only an unknown side throws, as in Parse.
        public static TjaAnalysis Analyze(string text, string requestedCourse = "Oni", string requestedSide = "single")
        {
            var analysis = new TjaAnalysis(text, requestedCourse, requestedSide);
            var sources = new List<LineSource>();
            var parser = new ChartParser(sources, analysis.diagnostics, analysis.SourceMap, analysis.measures);
            var read = new List<TjaLine>();
            analysis.Chart = parser.Run(text, Keep(TjaLines.Read(text ?? "", sources), read), requestedCourse, requestedSide);
            analysis.StartSpan = parser.StartSpan;
            analysis.EndSpan = parser.EndSpan;
            analysis.ReadLines = read;
            analysis.StartLineIndex = parser.StartLineIndex;
            return analysis;
        }

        // The lines as they are read, kept (TjaDocument compares them to reuse an analysis).
        static IEnumerable<TjaLine> Keep(IEnumerable<TjaLine> lines, List<TjaLine> read)
        {
            foreach (var line in lines)
            {
                read.Add(line);
                yield return line;
            }
        }

        // A warning with the file line it was met at: `… (measure n)` becomes `… (measure n, line l)`,
        // anything else gets ` (line l)`.
        internal static string AtLine(string warning, int line)
        {
            if (line <= 0) return warning;
            int measure = warning.LastIndexOf("(measure ", StringComparison.Ordinal);
            if (measure >= 0 && warning.IndexOf(')', measure) == warning.Length - 1)
                return warning.Substring(0, warning.Length - 1) + ", line " + line.ToString(CultureInfo.InvariantCulture) + ")";
            return warning + " (line " + line.ToString(CultureInfo.InvariantCulture) + ")";
        }

        // OpenTaiko #SCROLL (ParseReal / ParseComplex): a real number, or with an `i` a complex one
        // whose imaginary part is the vertical speed: `1+2i`, `1 + 2i`, `1 2i`, `2i`, `i`, `-i`,
        // exponents and `inf`. A positive imaginary part comes from below the judge.
        static readonly Regex ComplexParts = new Regex(@"(?<=[^e\s])\s*(?=[+-])|(?<=[^e\s])\s+(?=[^e\s])", RegexOptions.IgnoreCase);
        static readonly Regex Infinity = new Regex(@"inf(?:inity)?", RegexOptions.IgnoreCase);

        internal static bool Scroll(string value, out double x, out double y)
        {
            x = 0; y = 0;
            if (value.IndexOf('i') < 0) return TjaNumber.TryParse(Infinity.Replace(value, "Infinity"), out x);
            string[] parts = ComplexParts.Split(value);
            var ri = new double[2];
            int first = parts[0].EndsWith('i') ? 1 : 0;
            for (int k = 0, n = Math.Min(2 - first, parts.Length); k < n; k++)
            {
                string part = Infinity.Replace(parts[k], "Infinity").Replace(" ", "");
                if (first + k == 1 && part.EndsWith('i'))
                {
                    part = part.TrimEnd('i');
                    if (part.Length == 0 || part[part.Length - 1] == '+' || part[part.Length - 1] == '-') part += "1";
                }
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out ri[first + k])) return false;
            }
            x = ri[0]; y = ri[1];
            return true;
        }

        static readonly char[] Blanks = { ' ', '\t' };

        // A command's name, upper case: what stands before its first space or tab.
        static string CommandKey(string command)
        {
            int space = command.IndexOfAny(Blanks);
            return (space < 0 ? command : command.Substring(0, space)).ToUpperInvariant();
        }

        static ScrollMode? ScrollModeOf(string command)
        {
            string key = CommandKey(command);
            return key == "#NMSCROLL" ? ScrollMode.Normal : key == "#BMSCROLL" ? ScrollMode.BMScroll
                : key == "#HBSCROLL" ? ScrollMode.HBScroll : (ScrollMode?)null;
        }

        // The ASCII characters as strings, so a note line's tokens are not allocated one by one
        // (Unity's runtime has no such cache of its own).
        static readonly string[] AsciiStrings = Ascii();

        static string[] Ascii()
        {
            var strings = new string[128];
            for (int c = 0; c < strings.Length; c++) strings[c] = ((char)c).ToString();
            return strings;
        }

        // Each note character's value is its index here; lower case and other symbols are -1.
        const string Base36 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        // E, which OpenTaiko lists as unused: no note, but read.
        const int Unused = 14;
        // Whether a base-36 value is a NoteKind (Enum.IsDefined, without reflection per note).
        static readonly bool[] IsNoteKind = NoteKinds();

        static bool[] NoteKinds()
        {
            var kinds = new bool[Base36.Length];
            foreach (NoteKind kind in Enum.GetValues(typeof(NoteKind))) kinds[(int)kind] = true;
            return kinds;
        }

        // Whether a note line has a character that makes a note (not 0, 8 or E; not an inline
        // balloon count): SongInfo's HasNotes.
        internal static bool HasNote(string line)
        {
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '[')
                {
                    int close = line.IndexOf(']', i);
                    if (close < 0) break;
                    i = close;
                    continue;
                }
                int type = Base36.IndexOf(c);
                if (type > 0 && type != 8 && type != Unused && IsNoteKind[type]) return true;
            }
            return false;
        }

        // An inline balloon count, `7[10]` or `9[10]`: a whole number above zero; null if it is not one.
        static int? InlineBalloon(string token)
        {
            string count = token.Substring(2, token.Length - 3);
            if (count.Length == 0 || !count.All(c => c >= '0' && c <= '9')
                || !int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out int hits) || hits <= 0)
                return null;
            return hits;
        }

        static List<int>[] NewBalloonLists() => new[] { new List<int>(), new List<int>(), new List<int>() };

        // OpenTaiko ParseBalloon: comma-separated whole numbers, read up to the first empty item; a
        // number that does not read (such as `5.10`) rejects the whole line.
        static bool Balloons(string value, out List<int> hits)
        {
            hits = new List<int>();
            foreach (string item in value.Split(','))
            {
                if (item.Length == 0) break;
                if (!int.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return false;
                hits.Add(n);
            }
            return true;
        }

        sealed class TimingState
        {
            // RawMs and Clock16 are OpenTaiko's dbNowTime and dbNowBMScollTime: milliseconds from
            // #START and sixteenth notes, accumulated in its order of operations.
            public double RawMs, Beat, Bpm, Measure = 1, ScrollX = 1, ScrollY, Clock16;
            public double SuddenShow = double.PositiveInfinity, SuddenMove = double.PositiveInfinity;
            public bool Barline = true, Gogo;
            public ScrollMode Mode;
            public TimingState Copy() => (TimingState)MemberwiseClone();
        }

        // One block of a file, read in two passes: the lines up to the block's #END (headers in
        // force, the block's tokens), then the tokens. With `sources` (TjaLines.Read with
        // positions, for analysis) every token also gets its span in the file.
        internal sealed class ChartParser
        {
            readonly TaikoChart chart = new TaikoChart();
            readonly List<LineSource> sources;
            // The block's tokens: a command line whole, or one note character (with an inline
            // balloon count, `7[10]`); the file line of each, and its span when tracked.
            readonly List<string> tokens = new List<string>();
            readonly List<int> tokenLines = new List<int>();
            readonly List<TextSpan> tokenSpans;
            // Where the parser is: the file line (for the warnings) and, when tracked, the span.
            int line;
            TextSpan span;
            // Every problem where it is, for an analysis; null when parsing for play.
            readonly List<TjaDiagnostic> diagnostics;
            // Where each note, bar and event comes from, and the measures, for an analysis.
            readonly ChartSourceMap map;
            readonly List<ChartMeasure> measures;
            // The block's #START line and where it ends (#END, or what ended it), for an analysis.
            internal TextSpan StartSpan, EndSpan;
            // The index of the block's #START among the lines read.
            internal int StartLineIndex = -1;
            // TjaAnalysis.StateAt: the state before the first token at or after CaptureAt is read.
            internal int CaptureAt = -1;
            TjaPositionState captured;
            sealed class Captured : Exception { }
            // The note slot being read in the measure, and how many it has (0, 0 between measures).
            int slot, slotCount;
            long offsetMs;
            // The cursor's time: OpenTaiko's (int)dbNowTime, shifted by OFFSET, in seconds.
            double Now => ((long)state.RawMs - offsetMs) / 1000.0;
            // Each route reads its own list in order. A common balloon takes the Normal list's
            // number (the one played on every route) and moves every route's place past it.
            List<int>[] balloons;
            readonly int[] balloonIndex = new int[3];
            // The measure being read: its commands and notes (as token indices) in order, and how
            // many are notes.
            readonly List<int> pending = new List<int>();
            int pendingNotes;
            TimingState state, branchStart;
            ChartBranch branch;
            readonly bool[] routesSeen = new bool[3];
            BranchRoute route;
            bool hasRoute, branchBar;
            // Per route, the latest bar line and the latest long-note end after it, for
            // OpenTaiko's branch judgement time (CTja.GetBranchJudgeChipTime).
            readonly double?[] lastBar = new double?[3], rollEndAfterBar = new double?[3];
            // OpenTaiko keeps the open roll per route (nNowRollCountBranch): a head outside a route
            // opens on all three, a head on a route only there. Closed heads are remembered so a
            // second route closing a shared head keeps the first end.
            readonly ChartNote[] openRoll = new ChartNote[3];
            readonly HashSet<ChartNote> closedRolls = new HashSet<ChartNote>();
            int measure;
            readonly List<ChartEntry> common = new List<ChartEntry>();
            List<ChartEntry>[] routeLists;
            // Inside a branch but before its first #N/#E/#M the notes are common (OpenTaiko
            // IsEndedBranching): they go to every route and the routes then start over them.
            bool InRoute => branch != null && hasRoute;
            int BranchId => InRoute ? branch.Id : -1;
            List<ChartEntry> Entries => InRoute ? routeLists[(int)route] : common;
            bool warnedCommonInBranch;

            public ChartParser(List<LineSource> sources, List<TjaDiagnostic> diagnostics = null, ChartSourceMap map = null, List<ChartMeasure> measures = null)
            {
                this.sources = sources;
                this.diagnostics = diagnostics;
                this.map = map;
                this.measures = measures;
                if (sources != null) tokenSpans = new List<TextSpan>();
            }

            internal TaikoChart Chart => chart;

            // A warning: TaikoChart.Warnings keeps each text once, with the line it was first met at;
            // an analysis keeps every occurrence where it is.
            void Warn(TjaDiagnosticKind kind, params object[] args)
            {
                string message = kind.Message(args);
                if (warned.Add(message)) chart.Warnings.Add(AtLine(message, line));
                diagnostics?.Add(new TjaDiagnostic(kind, message, args, line, span));
            }

            // An error: the block cannot be played as written. For play it is thrown, as
            // TjaParser.Parse always has; an analysis notes it and the caller recovers.
            void Error(TjaDiagnosticKind kind, params object[] args)
            {
                string message = kind.Message(args);
                if (diagnostics == null)
                {
                    if (kind.NotSupported) throw new NotSupportedException(message);
                    throw new FormatException(message);
                }
                diagnostics.Add(new TjaDiagnostic(kind, message, args, line, span));
            }

            // A number the block needs; one that does not read is an error with the runtime's
            // message (thrown for play; false for an analysis).
            bool ReadNumber(string value, out double result)
            {
                if (diagnostics == null) { result = Number(value); return true; }
                try { result = Number(value); return true; }
                catch (FormatException e)
                {
                    diagnostics.Add(new TjaDiagnostic(TjaDiagnosticKind.NumberUnreadable, e.Message, new object[] { e.Message }, line, span));
                    result = 0;
                    return false;
                }
            }

            // Where a header's value is, for its problems.
            void AtHeader(TjaLine header, LineSource? source)
            {
                line = header.Line;
                if (source == null) return;
                string text = header.Text;
                int from = text.IndexOf(':') + 1;
                while (from < text.Length && char.IsWhiteSpace(text[from])) from++;
                span = source.Value.Span(from, text.Length);
            }

            public TaikoChart Run(string text, IEnumerable<TjaLine> lines, string requestedCourse, string requestedSide)
            {
                // An analysis of a file without the block has no chart.
                if (string.IsNullOrWhiteSpace(text)) { Error(TjaDiagnosticKind.EmptyChart); return null; }
                if (Array.IndexOf(ChartSide.All, requestedSide) < 0) throw new ArgumentException($"Unknown side '{requestedSide}'.", nameof(requestedSide));
                int endLine = 0;
                var endSpan = default(TextSpan);
                // Where the BPM, OFFSET and LEVEL in force are written.
                TjaLine bpmLine = default, offsetLine = default, levelLine = default;
                LineSource? bpmSource = null, offsetSource = null, levelSource = null;
                // Balloon hits per route (Normal, Expert, Master); headers before any COURSE are
                // every course's defaults, and each COURSE starts again from them.
                var globalBalloons = NewBalloonLists();
                balloons = NewBalloonLists();
                bool seenCourse = false;
                string course = "Oni";
                // BPM, OFFSET and LEVEL as written; only the values in force for the block played are
                // read, so an unreadable one elsewhere in the file (another course) does not fail it.
                string bpm = null, offset = null, level = null;
                bool reading = false, found = false, ended = false;
                // A scroll command after the course's COURSE: and before its #START sets how it begins.
                ScrollMode startMode = ScrollMode.Normal;
                int index = -1;
                // The first block of the requested course on the requested side is played.
                foreach (var line in lines)
                {
                    index++;
                    this.line = line.Line;
                    if (sources != null) span = sources[index].Span(0, line.Implicit ? 0 : line.Text.Length);
                    if (line.Kind == TjaLineKind.Start)
                    {
                        reading = SameCourse(course, requestedCourse) && ChartSide.OfStart(line.Value) == requestedSide;
                        if (reading)
                        {
                            found = true;
                            StartSpan = span;
                            StartLineIndex = index;
                            chart.Course = Course(course).Split('_')[0];
                            if (level != null) AtHeader(levelLine, levelSource);
                            // Recovery: an unreadable LEVEL is 0.
                            chart.Level = level != null && ReadNumber(level, out double stars) ? (int)stars : 0;
                        }
                        continue;
                    }
                    if (line.Kind == TjaLineKind.End)
                    {
                        if (!reading) continue;
                        if (line.Implicit) Warn(TjaDiagnosticKind.EndMissing);
                        endLine = line.Line;
                        endSpan = span;
                        EndSpan = span;
                        ended = true; break;
                    }
                    if (line.Kind == TjaLineKind.Body)
                    {
                        if (!reading) continue;
                        if (line.Text[0] == '#') AddToken(line.Text, line.Line, sources == null ? default : sources[index].Span(0, line.Text.Length));
                        else NoteTokens(line, index);
                        continue;
                    }
                    if (line.Kind == TjaLineKind.Other)
                    {
                        if (seenCourse && ScrollModeOf(line.Text) is ScrollMode mode) startMode = mode;
                        continue;
                    }
                    if (line.Kind != TjaLineKind.Header) continue;
                    string value = line.Value;
                    switch (line.Key)
                    {
                        case "TITLE": chart.Title = value; break;
                        case "SUBTITLE": chart.Subtitle = value.TrimStart('-', '+'); break;
                        case "BPM": bpm = value; bpmLine = line; bpmSource = sources?[index]; break;
                        case "OFFSET": offset = value; offsetLine = line; offsetSource = sources?[index]; break;
                        case "COURSE":
                            course = value; level = null; seenCourse = true; startMode = ScrollMode.Normal;
                            for (int r = 0; r < 3; r++) balloons[r] = new List<int>(globalBalloons[r]);
                            break;
                        case "LEVEL": level = value; levelLine = line; levelSource = sources?[index]; break;
                        // OpenTaiko: BALLOON is the Normal route's list; each header replaces its route's list.
                        case "BALLOON": case "BALLOONNOR": case "BALLOONEXP": case "BALLOONMAS":
                            int route = line.Key == "BALLOONEXP" ? 1 : line.Key == "BALLOONMAS" ? 2 : 0;
                            if (!Balloons(value, out var hits)) { Warn(TjaDiagnosticKind.BalloonHeaderUnreadable, line.Text); break; }
                            balloons[route] = hits;
                            if (!seenCourse) globalBalloons[route] = new List<int>(hits);
                            break;
                    }
                }
                if (!found || !ended)
                {
                    line = 0; span = default;
                    Error(TjaDiagnosticKind.CourseNotFound, requestedCourse, requestedSide);
                    return null;
                }
                // Recovery: an unreadable BPM or OFFSET keeps the default (120, 0); a BPM that is not
                // positive and finite is 120.
                if (bpm != null)
                {
                    AtHeader(bpmLine, bpmSource);
                    if (ReadNumber(bpm, out double tempo)) chart.Bpm = tempo;
                }
                // OpenTaiko keeps OFFSET in whole milliseconds, cut toward zero.
                if (offset != null)
                {
                    AtHeader(offsetLine, offsetSource);
                    if (ReadNumber(offset, out double seconds)) chart.Offset = (int)(seconds * 1000) / 1000.0;
                }
                if (!(chart.Bpm > 0) || double.IsInfinity(chart.Bpm))
                {
                    if (bpm != null) AtHeader(bpmLine, bpmSource);
                    else { line = 0; span = default; }
                    Error(TjaDiagnosticKind.BpmNotPositive);
                    chart.Bpm = 120;
                }

                offsetMs = (long)Math.Round(chart.Offset * 1000);
                state = new TimingState { Bpm = chart.Bpm, Mode = startMode };
                chart.NoteLists.Add(common);
                AddClockPoint();
                Read(endLine, endSpan);
                return chart;
            }

            internal IReadOnlyList<string> Tokens => tokens;
            internal IReadOnlyList<int> TokenLines => tokenLines;
            internal IReadOnlyList<TextSpan> TokenSpans => tokenSpans;

            void AddToken(string token, int line, TextSpan span)
            {
                tokens.Add(token);
                tokenLines.Add(line);
                tokenSpans?.Add(span);
            }

            // A note line's tokens: one per character, spaces skipped. `[…]` (not in OpenTaiko) stays
            // on the note before it, as in `7[10]`, and must close on the same line.
            void NoteTokens(TjaLine line, int index)
            {
                string text = line.Text;
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (char.IsWhiteSpace(c)) continue;
                    if (c != '[')
                    {
                        AddToken(c < AsciiStrings.Length ? AsciiStrings[c] : c.ToString(), line.Line, sources == null ? default : sources[index].Span(i, i + 1));
                        continue;
                    }
                    int close = text.IndexOf(']', i);
                    this.line = line.Line;
                    // Recovery: an unclosed [ drops the rest of its line; a [ after no note drops up to its ].
                    if (close < 0)
                    {
                        if (sources != null) span = sources[index].Span(i, text.Length);
                        Error(TjaDiagnosticKind.UnclosedBracket, text);
                        break;
                    }
                    if (i == 0 || tokens[tokens.Count - 1].Length != 1 || tokens[tokens.Count - 1] == ",")
                    {
                        if (sources != null) span = sources[index].Span(i, close + 1);
                        Error(TjaDiagnosticKind.BracketWithoutNote, text);
                        i = close;
                        continue;
                    }
                    tokens[tokens.Count - 1] += new string(text.Substring(i, close - i + 1).Where(ch => !char.IsWhiteSpace(ch)).ToArray());
                    if (tokenSpans != null) tokenSpans[tokenSpans.Count - 1] = TextSpan.FromBounds(tokenSpans[tokenSpans.Count - 1].Start, sources[index].SourceOffset(close) + 1);
                    i = close;
                }
            }

            // The state where a position is (TjaAnalysis.StateAt): the block is read up to the first
            // token at or after it.
            public TjaPositionState Capture(string text, IEnumerable<TjaLine> lines, string requestedCourse, string requestedSide, int offset)
            {
                CaptureAt = offset;
                try { Run(text, lines, requestedCourse, requestedSide); }
                catch (Captured) { }
                return captured;
            }

            void Checkpoint()
            {
                if (CaptureAt >= 0 && span.Start >= CaptureAt) Capture();
            }

            void Capture()
            {
                int list = InRoute ? (int)route : 0;
                int next = balloonIndex[list];
                var open = openRoll[InRoute ? (int)route : 0];
                captured = new TjaPositionState
                {
                    Time = Now, Beat = state.Beat, Bpm = state.Bpm, MeasureLength = state.Measure,
                    ScrollX = state.ScrollX, ScrollY = state.ScrollY, ScrollMode = state.Mode,
                    Barline = state.Barline, Gogo = state.Gogo, SuddenShow = state.SuddenShow, SuddenMove = state.SuddenMove,
                    Measure = measure, Slot = slot, Slots = slotCount,
                    BranchId = branch?.Id ?? -1, Route = InRoute ? route : (BranchRoute?)null,
                    OpenRoll = open?.Kind,
                    BalloonList = BalloonHeaders[list], BalloonIndex = next, NextBalloonHits = next < balloons[list].Count ? balloons[list][next] : 5,
                };
                throw new Captured();
            }

            // Where token t is.
            void At(int t)
            {
                line = tokenLines[t];
                if (tokenSpans != null) span = tokenSpans[t];
            }

            // A BPM point of the beat clock where the cursor is, on the current route or, outside a
            // route, on all of them.
            void AddClockPoint() => chart.ScrollClocks.Add(new ChartClockPoint { Time = Now, RawMs = state.RawMs, Clock = state.Clock16 / 16,
                Bpm = state.Bpm, BranchId = branch != null && hasRoute ? branch.Id : -1, Route = route });

            void Read(int endLine, TextSpan endSpan)
            {
                for (int t = 0; t < tokens.Count; t++)
                {
                    string token = tokens[t];
                    At(t);
                    if (token == ",") Flush();
                    else if (token[0] == '#' && IsBranchCommand(CommandKey(token)))
                    {
                        FlushCommands();
                        // The commands before it moved the position; the branch command is here.
                        At(t);
                        Checkpoint();
                        Command(token);
                    }
                    else
                    {
                        pending.Add(t);
                        if (token[0] != '#') pendingNotes++;
                    }
                }
                // OpenTaiko reads notes after the last comma as a measure of their own.
                if (pendingNotes > 0) { Warn(TjaDiagnosticKind.LastMeasureWithoutComma); Flush(atComma: false); }
                else FlushCommands();
                // A position after the last token: the state the block ends in.
                if (CaptureAt >= 0) Capture();
                line = endLine; span = endSpan;
                if (branch != null) EndBranch(); // #BRANCHEND is optional before #END / the next branch.
                // OpenTaiko (TaikoJiro compatibility): #END ends unended rolls.
                for (int k = 0, open = OpenRolls(); k < open; k++) { Warn(TjaDiagnosticKind.RollEndedByEnd); CloseRoll(openHeads[k], Entries); }
                chart.Duration = Math.Max(chart.Duration, Now);
                chart.Notes.Sort((a, b) => a.Time.CompareTo(b.Time));
                chart.Bars.Sort((a, b) => a.Time.CompareTo(b.Time));
                SortStable(chart.Gogos, g => g.Time);
                SortStable(chart.Tempos, t => t.Time);
                SortStable(chart.ScrollClocks, c => c.Time);
                SortStable(chart.LaneAlphas, a => a.StartTime);
                SortStable(chart.JudgeMoves, m => m.Time);
                JudgeShift.Link(chart.JudgeMoves);
                NoteMoji.Assign(chart);
            }

            static bool IsBranchCommand(string key)
                => key == "#BRANCHSTART" || key == "#BRANCHEND" || key == "#N" || key == "#E" || key == "#M";

            // By time, keeping source order at one time (unlike List.Sort). Without branches the
            // events come in time order already, and are left as they are.
            static void SortStable<T>(List<T> list, Func<T, double> time)
            {
                int i = 1;
                while (i < list.Count && time(list[i - 1]).CompareTo(time(list[i])) <= 0) i++;
                if (i >= list.Count) return;
                var sorted = list.OrderBy(time).ToList();
                list.Clear(); list.AddRange(sorted);
            }

            void FlushCommands()
            {
                if (pendingNotes > 0)
                {
                    // Recovery: the notes before the branch boundary are a measure of their own.
                    Error(TjaDiagnosticKind.MissingCommaBeforeBranch);
                    Flush(atComma: false);
                    return;
                }
                for (int i = 0; i < pending.Count; i++)
                {
                    At(pending[i]);
                    Checkpoint();
                    Command(tokens[pending[i]]);
                }
                pending.Clear();
            }

            // The routes being written: the current one, or all three outside a route.
            int FirstRoute => InRoute ? (int)route : 0;
            int LastRoute => InRoute ? (int)route : 2;

            // The distinct heads still open on the routes being written (one head may sit on
            // several), in route order, into openHeads; their count. Closing one of them leaves the
            // others open, so the caller may close them as it goes through the list.
            readonly ChartNote[] openHeads = new ChartNote[3];

            int OpenRolls()
            {
                int count = 0;
                for (int r = FirstRoute, last = LastRoute; r <= last; r++)
                {
                    var head = openRoll[r];
                    if (head == null) continue;
                    bool seen = false;
                    for (int k = 0; k < count; k++) seen |= openHeads[k] == head;
                    if (!seen) openHeads[count++] = head;
                }
                return count;
            }

            // Ends a roll here on the routes being written. A head shared by several routes keeps
            // the end the first route gave it (one ChartNote has one EndTime); a later route that
            // ends it elsewhere is warned about, and still gets the tail entry in its own list.
            void CloseRoll(ChartNote note, List<ChartEntry> entries)
            {
                if (closedRolls.Add(note))
                {
                    note.EndTime = Now;
                    note.TailBpm = state.Bpm;
                    note.EndScrollClock = state.Clock16 / 16;
                }
                else if (note.EndTime != Now) Warn(TjaDiagnosticKind.SharedRollEndsDiffer, measure);
                for (int r = FirstRoute, last = LastRoute; r <= last; r++)
                    if (openRoll[r] == note) { openRoll[r] = null; rollEndAfterBar[r] = Math.Max(rollEndAfterBar[r] ?? Now, Now); }
                entries.Add(new ChartEntry(note, true));
                map?.AddTail(note, span);
            }

            void EndBranch()
            {
                // OpenTaiko: a branch without #N plays an empty normal route.
                if (!routesSeen[0]) Warn(TjaDiagnosticKind.BranchWithoutNormal, branch.Id);
                // Match Fanmade's image renderer: omitted E uses N; omitted M uses E.
                if (!routesSeen[1]) branch.Routes[1] = BranchRoute.Normal;
                if (!routesSeen[2]) branch.Routes[2] = branch.Routes[1];
                branch.EndTime = Now;
                // As in OurTaikoPlayer, common notes continue from the final authored route.
                branch = null; hasRoute = false; branchBar = false;
                // OpenTaiko GotoBranchEnd: every route's beat clock continues from here.
                AddClockPoint();
            }

            void StartBranch(string arg)
            {
                if (branch != null) EndBranch();
                AddClockPoint();
                branchStart = state.Copy();
                branch = new ChartBranch { Id = chart.Branches.Count, Time = Now };
                ReadCondition(branch, arg);
                branch.DecisionTime = JudgeTime(branch.Condition == BranchCondition.Drumroll || branch.Condition == BranchCondition.BalloonHits);
                chart.Branches.Add(branch);
                map?.Add(branch, span);
                routeLists = new[] { new List<ChartEntry>(), new List<ChartEntry>(), new List<ChartEntry>() };
                chart.NoteLists.AddRange(routeLists);
                Array.Clear(routesSeen, 0, routesSeen.Length);
                hasRoute = false; warnedCommonInBranch = false;
            }

            // OpenTaiko judges a branch at the latest bar line before it (the start of the measure
            // before #BRANCHSTART), or four beats before the chart if there is none. For drumroll
            // and balloon conditions it waits for a roll ending in that last measure. Never after
            // the branch itself.
            double JudgeTime(bool waitForRolls)
            {
                double judge = lastBar.Max() ?? ((long)(-240000.0 / chart.Bpm) - offsetMs) / 1000.0;
                double? rollEnd = rollEndAfterBar.Max();
                if (waitForRolls && rollEnd > judge) judge = Math.Min(rollEnd.Value, Now);
                return judge;
            }

            // Marks the current route, or every route outside a branch.
            void Track(double?[] perRoute, double? value, bool latest = false)
            {
                for (int r = 0; r < 3; r++)
                    if (!InRoute || r == (int)route) perRoute[r] = latest ? Math.Max(perRoute[r] ?? value.Value, value.Value) : value;
            }

            // OpenTaiko CTja #BRANCHSTART: `type, expert, master[, l|m]`; a missing comma after the
            // type is tolerated. An empty or unreadable condition keeps the current route; unlike
            // OpenTaiko, which drops the whole command, an unknown type or range does the same.
            void ReadCondition(ChartBranch target, string arg)
            {
                target.Condition = BranchCondition.None;
                arg = Regex.Replace(arg, @"(?<=^\s*[a-zA-Z]+)\s*(?=\d)", ",");
                if (string.IsNullOrWhiteSpace(arg)) return;
                string[] parts = arg.Split(',').Select(p => p.Trim()).ToArray();
                if (parts.Length < 3 || parts[0].Length == 0
                    || !TjaNumber.TryParse(parts[1], out double expert) || !TjaNumber.TryParse(parts[2], out double master))
                {
                    Warn(TjaDiagnosticKind.BranchConditionUnreadable, arg);
                    return;
                }
                bool upper = char.IsUpper(parts[0][0]);
                BranchCondition? condition = parts[0].ToLowerInvariant() switch
                {
                    "p" => BranchCondition.Accuracy, "pp" => BranchCondition.PercentPerfect,
                    "jp" => BranchCondition.JudgePerfect, "d" => BranchCondition.JudgePerfect,
                    "jg" => BranchCondition.JudgeGood, "jb" => BranchCondition.JudgeBad,
                    "r" => BranchCondition.Drumroll, "rb" => BranchCondition.BalloonHits,
                    "s" => BranchCondition.Score, _ => null,
                };
                string range = parts.Length > 3 ? parts[3] : "";
                if (condition == null || range != "" && range != "l" && range != "m")
                {
                    Warn(TjaDiagnosticKind.BranchConditionUnreadable, arg);
                    return;
                }
                target.Condition = condition.Value;
                // s is never big-only; d always is.
                target.BigOnly = parts[0].ToLowerInvariant() == "d" || upper && condition != BranchCondition.Score;
                target.Less = range == "l";
                target.ExpertThreshold = expert; target.MasterThreshold = master;
            }

            // The warnings' texts so far (TaikoChart.Warnings keeps each once).
            readonly HashSet<string> warned = new HashSet<string>();

            void SelectRoute(BranchRoute selected)
            {
                // Recovery: a route marker out of a branch, or written twice, is ignored.
                if (branch == null) { Error(TjaDiagnosticKind.RouteWithoutBranch); return; }
                if (routesSeen[(int)selected]) { Error(TjaDiagnosticKind.DuplicateRoute, selected); return; }
                routesSeen[(int)selected] = true;
                state = branchStart.Copy(); route = selected; hasRoute = true; branchBar = true;
            }

            ChartNote NewNote() => new ChartNote { Time = Now, EndTime = Now, RawMs = state.RawMs,
                Beat = state.Beat, Bpm = state.Bpm, ScrollX = state.ScrollX, ScrollY = state.ScrollY,
                ScrollMode = state.Mode, ScrollClock = state.Clock16 / 16, EndScrollClock = state.Clock16 / 16,
                SuddenShow = state.SuddenShow, SuddenMove = state.SuddenMove,
                BranchId = BranchId, Route = route };

            static readonly string[] BalloonHeaders = { "BALLOON", "BALLOONEXP", "BALLOONMAS" };

            // A route without a list, or past its end, gives 5 hits (OpenTaiko's default for an
            // empty list; past the end it leaves 0, which no balloon can be popped at).
            int NextBalloon(int r)
            {
                var list = balloons[r];
                int i = balloonIndex[r]++;
                return i < list.Count ? list[i] : 5;
            }

            void SetGogo(bool on)
            {
                state.Gogo = on;
                var gogo = new ChartGogo { Time = Now, On = on, BranchId = BranchId, Route = route };
                chart.Gogos.Add(gogo);
                map?.Add(gogo, span);
            }

            ChartNote AddBar()
            {
                if (branch != null && !hasRoute && !warnedCommonInBranch)
                {
                    // OpenTaiko: these measures are common, and each route then starts over them.
                    Warn(TjaDiagnosticKind.CommonNotesInBranch, measure);
                    warnedCommonInBranch = true;
                }
                var bar = NewNote(); bar.Display = state.Barline; bar.IsBranchStart = branchBar;
                bar.SuddenShow = bar.SuddenMove = double.PositiveInfinity;
                chart.Bars.Add(bar);
                Entries.Add(new ChartEntry(bar, false));
                if (InRoute && branch.FirstEntries[(int)route] == null) branch.FirstEntries[(int)route] = bar;
                branchBar = false;
                Track(lastBar, Now);
                Track(rollEndAfterBar, null);
                return bar;
            }

            // One command. Those that set a number the chart cannot be read without (BPMCHANGE,
            // MEASURE) or that break the branch structure fail the block; the others, when they do
            // not read, are dropped with the command as a warning (as OpenTaiko drops them); an
            // unknown command is warned about by its name.
            void Command(string command)
            {
                int space = command.IndexOfAny(Blanks);
                string key = CommandKey(command);
                string arg = space < 0 ? "" : command.Substring(space + 1).Trim();
                bool read = true;
                switch (key)
                {
                    case "#BPMCHANGE": BpmChange(arg); break;
                    case "#MEASURE": Measure(command, arg); break;
                    case "#SCROLL": read = Scroll(arg); break;
                    case "#DELAY": read = Delay(arg); break;
                    case "#GOGOSTART": SetGogo(true); break;
                    case "#GOGOEND": SetGogo(false); break;
                    case "#BARLINEOFF": state.Barline = false; break;
                    case "#BARLINEON": state.Barline = true; break;
                    case "#BRANCHSTART": StartBranch(arg); break;
                    case "#BRANCHEND": if (branch == null) Error(TjaDiagnosticKind.BranchEndWithoutStart); else EndBranch(); break;
                    case "#N": SelectRoute(BranchRoute.Normal); break;
                    case "#E": SelectRoute(BranchRoute.Expert); break;
                    case "#M": SelectRoute(BranchRoute.Master); break;
                    case "#SECTION": Section(); break;
                    case "#LEVELHOLD": LevelHold(); break;
                    case "#NMSCROLL": case "#BMSCROLL": case "#HBSCROLL": state.Mode = ScrollModeOf(key).Value; break;
                    case "#JPOSSCROLL": read = JposScroll(arg); break;
                    case "#SUDDEN": read = Sudden(arg); break;
                    case "#LANE_ALPHA": read = LaneAlphaCommand(command, arg); break;
                    default: Warn(TjaDiagnosticKind.UnknownCommand, key); break;
                }
                if (!read) Warn(TjaDiagnosticKind.CommandDropped, command);
            }

            // Recovery: a BPMCHANGE or MEASURE that does not read is dropped.
            void BpmChange(string arg)
            {
                if (!ReadNumber(arg, out double bpm)) return;
                if (!(bpm > 0) || double.IsInfinity(bpm)) { Error(TjaDiagnosticKind.InvalidBpmChange); return; }
                state.Bpm = bpm;
                var tempo = new ChartTempo { Time = Now, ResumeTime = Now, Beat = state.Beat, Bpm = state.Bpm, BranchId = BranchId, Route = route };
                chart.Tempos.Add(tempo);
                map?.Add(tempo, span);
                AddClockPoint();
            }

            void Section()
            {
                var section = new ChartSection { Time = Now, BranchId = BranchId, Route = route };
                chart.Sections.Add(section);
                map?.Add(section, span);
            }

            // OpenTaiko keeps the two numbers as floats and divides them as floats.
            void Measure(string command, string arg)
            {
                var parts = arg.Split('/');
                if (parts.Length < 2) { Error(TjaDiagnosticKind.MeasureWithoutSlash, command); return; }
                if (!ReadNumber(parts[0], out double beats) || !ReadNumber(parts[1], out double unit)) return;
                double length = (float)beats / (float)unit;
                if (!(length > 0) || double.IsInfinity(length)) { Error(TjaDiagnosticKind.InvalidMeasure); return; }
                state.Measure = length;
            }

            // An unreadable SCROLL keeps the current one (OpenTaiko drops the command).
            bool Scroll(string arg)
            {
                if (!TjaParser.Scroll(arg, out double scrollX, out double scrollY)) return false;
                state.ScrollX = scrollX; state.ScrollY = scrollY;
                return true;
            }

            bool Delay(string arg)
            {
                if (!TjaNumber.TryParse(arg, out double delay) || double.IsNaN(delay) || double.IsInfinity(delay)) return false;
                double start = Now;
                state.RawMs += delay * 1000;
                var tempo = new ChartTempo { Time = start, ResumeTime = Now, Beat = state.Beat, Bpm = state.Bpm, BranchId = BranchId, Route = route };
                chart.Tempos.Add(tempo);
                map?.Add(tempo, span);
                // The beat clock keeps running through a delay (OpenTaiko dbNowBMScollTime).
                state.Clock16 += delay * 1000 * state.Bpm / 15000;
                return true;
            }

            // OpenTaiko: inside a route it holds that route once the branch chooses it; anywhere
            // else it holds the route being played from its time.
            void LevelHold()
            {
                if (InRoute) branch.HoldRoutes[(int)route] = true;
                else chart.LevelHolds.Add(Now);
            }

            // OpenTaiko: `#JPOSSCROLL seconds move [direction]`; move is a real (across) or a
            // complex number (across and down) in 1280-wide pixels; direction 0 (the default) turns
            // it around, as notes move. Moves on every route count.
            bool JposScroll(string arg)
            {
                var jpos = arg.Split(Blanks, StringSplitOptions.RemoveEmptyEntries);
                int direction = 0;
                if (jpos.Length < 2 || !TjaNumber.TryParse(Infinity.Replace(jpos[0], "Infinity"), out double seconds)
                    || !TjaParser.Scroll(jpos[1], out double x, out double y)
                    || jpos.Length >= 3 && !int.TryParse(jpos[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out direction))
                    return false;
                if (direction == 0) { x = -x; y = -y; }
                var judgeMove = new ChartJudgeMove { Time = Now, Duration = Math.Max(0, 1000 * seconds), MoveX = x, MoveY = y };
                chart.JudgeMoves.Add(judgeMove);
                map?.Add(judgeMove, span);
                return true;
            }

            // OpenTaiko: `#SUDDEN show move` in seconds, both required. Less than a millisecond
            // means unset; the move time is cut to whole milliseconds.
            bool Sudden(string arg)
            {
                var times = arg.Split(Blanks, StringSplitOptions.RemoveEmptyEntries);
                if (times.Length < 2 || !TjaNumber.TryParse(times[0], out double show) || !TjaNumber.TryParse(times[1], out double move))
                    return false;
                state.SuddenShow = Math.Abs(Math.Truncate(show * 1000)) >= 1 ? show : double.PositiveInfinity;
                state.SuddenMove = Math.Abs(Math.Truncate(move * 1000)) >= 1 ? Math.Truncate(move * 1000) / 1000 : double.PositiveInfinity;
                return true;
            }

            // OurTaiko (not in OpenTaiko): `#LANE_ALPHA target[%] [ms]`. target is the lane
            // background's opacity, 0–100 percent; ms (default 0, cut to whole milliseconds) fades
            // from here for that long, or with a negative value fades from that long before up to
            // here. Anything else (missing, unreadable or out-of-range numbers, a third argument)
            // drops the command with a warning, as SCROLL is.
            bool LaneAlphaCommand(string command, string arg)
            {
                var args = arg.Split(Blanks, StringSplitOptions.RemoveEmptyEntries);
                double ms = 0;
                if (args.Length < 1 || args.Length > 2
                    || !TjaNumber.TryParse(args[0].EndsWith('%') ? args[0].Substring(0, args[0].Length - 1) : args[0], out double percent)
                    || !(percent >= 0 && percent <= 100)
                    || args.Length == 2 && (!TjaNumber.TryParse(args[1], out ms) || double.IsNaN(ms) || double.IsInfinity(ms)))
                    return false;
                double fade = Math.Truncate(ms) / 1000;
                var alpha = new ChartLaneAlpha { Time = Now, StartTime = Math.Min(Now, Now + fade), EndTime = Math.Max(Now, Now + fade),
                    Alpha = percent / 100, BranchId = BranchId, Route = route };
                // The game knows a route only once the branch is decided.
                if (InRoute && alpha.StartTime < branch.DecisionTime)
                    Warn(TjaDiagnosticKind.LaneAlphaBeforeDecision, branch.Id, command);
                chart.LaneAlphas.Add(alpha);
                map?.Add(alpha, span);
                return true;
            }

            // The measure waiting: its commands and notes, at the comma (or, without one, where it
            // ends). A bar line's source is its measure, from its first note to its comma.
            void Flush(bool atComma = true)
            {
                int slots = pendingNotes, index = 0, commaLine = line;
                var commaSpan = span;
                bool addedBar = false;
                ChartNote bar = null;
                int barStart = 0;
                double barLength = 0;
                slotCount = slots;
                for (int i = 0; i < pending.Count; i++)
                {
                    string token = tokens[pending[i]];
                    At(pending[i]);
                    slot = index;
                    Checkpoint();
                    if (token[0] == '#') { Command(token); continue; }
                    if (!addedBar)
                    {
                        bar = AddBar();
                        barStart = span.Start;
                        barLength = state.Measure;
                        addedBar = true;
                    }
                    // A note character is its NoteKind in base 36 (G is 16), upper case only as in OpenTaiko;
                    // 0 (blank), 8 (roll end) and E (unused) are read but make no note.
                    int type = Base36.IndexOf(token[0]);
                    // Recovery: a note the parser does not know is a blank; an inline count on a note
                    // that is not a balloon, or one that does not read, is ignored.
                    if (type != 0 && type != 8 && type != Unused && (type < 0 || !IsNoteKind[type]))
                    {
                        Error(TjaDiagnosticKind.UnsupportedNote, token);
                        goto advance;
                    }
                    int? inlineHits = null;
                    if (token.Length > 1)
                    {
                        if (type != 7 && type != 9) Error(TjaDiagnosticKind.CountOnNonBalloon, token);
                        else
                        {
                            inlineHits = InlineBalloon(token);
                            if (inlineHits == null) Error(TjaDiagnosticKind.InvalidBalloonCount, token);
                        }
                    }
                    // OpenTaiko's roll rules (CTja, TaikoJiro compatibility): an 8 ends the open roll of
                    // the routes being written and is a blank where none is open; a roll head while a
                    // roll is open is a blank (the 9…9…8 kusudama); any other note ends the open roll
                    // where it stands and is then placed itself.
                    if (type == 8)
                    {
                        int open = OpenRolls();
                        for (int k = 0; k < open; k++) CloseRoll(openHeads[k], Entries);
                        if (open == 0) Warn(TjaDiagnosticKind.StrayRollEnd, measure);
                    }
                    else if (type != 0)
                    {
                        bool blank = false;
                        for (int k = 0, open = OpenRolls(); k < open; k++)
                        {
                            if (type >= 5 && type <= 9) { Warn(TjaDiagnosticKind.RollHeadInRoll, measure); blank = true; }
                            else { Warn(TjaDiagnosticKind.RollEndedByNote, type, measure); CloseRoll(openHeads[k], Entries); }
                        }
                        // OpenTaiko's unknown symbol: it ends an open roll as a note would, then is a blank.
                        if (type == Unused) { Warn(TjaDiagnosticKind.UnusedNoteE, measure); blank = true; }
                        if (blank) goto advance;
                        var note = NewNote(); note.Kind = (NoteKind)type;
                        if (note.IsBalloon)
                        {
                            // The list the count comes from: the route's, or outside a route the Normal one.
                            int list = InRoute ? (int)route : 0;
                            bool listed = balloonIndex[list] < balloons[list].Count;
                            if (InRoute) note.BalloonHits = NextBalloon(list);
                            else { note.BalloonHits = NextBalloon(0); NextBalloon(1); NextBalloon(2); }
                            // An inline count still takes its place in the lists.
                            if (inlineHits.HasValue) note.BalloonHits = inlineHits.Value;
                            else if (!listed) Warn(TjaDiagnosticKind.BalloonWithoutCount, BalloonHeaders[list], measure);
                        }
                        chart.Notes.Add(note);
                        map?.AddNote(note, span);
                        Entries.Add(new ChartEntry(note, false));
                        if (note.IsLong) for (int r = FirstRoute, last = LastRoute; r <= last; r++) openRoll[r] = note;
                    }
                    advance:
                    state.RawMs += 15000.0 / state.Bpm * state.Measure * (16.0 / slots);
                    state.Beat += 4.0 * state.Measure / Math.Max(1, slots);
                    state.Clock16 += state.Measure * (16.0 / slots);
                    index++;
                }
                // The comma: the measure's slots are all read.
                slot = index;
                if (atComma) { span = commaSpan; Checkpoint(); }
                if (index == 0)
                {
                    line = commaLine; span = commaSpan;
                    bar = AddBar();
                    barStart = span.Start;
                    barLength = state.Measure;
                    state.RawMs += 15000.0 / state.Bpm * state.Measure * 16.0;
                    state.Beat += 4 * state.Measure;
                    state.Clock16 += state.Measure * 16.0;
                }
                chart.Duration = Math.Max(chart.Duration, Now);
                var measureSpan = TextSpan.FromBounds(barStart, atComma ? commaSpan.End : span.End);
                map?.Add(bar, measureSpan);
                measures?.Add(new ChartMeasure
                {
                    Index = measure, Span = measureSpan, StartTime = bar.Time, EndTime = Now, Bpm = bar.Bpm, Length = barLength,
                    Slots = slots, BranchId = bar.BranchId, Route = bar.Route, Bar = bar,
                });
                measure++;
                pending.Clear();
                pendingNotes = 0;
                slot = slotCount = 0;
            }
        }
    }
}
