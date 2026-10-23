using System;
using System.Collections.Generic;
using System.Linq;

namespace OurTaiko
{
    // Difficulty columns of the original song select (src/libs/global_data.h Difficulty);
    // Edit is the Ura course that shares the Oni column.
    public enum Difficulty { Back = -3, Modifier = -2, Neiro = -1, Easy = 0, Normal, Hard, Oni, Ura }

    // One #START block of a TJA file, in file order. Every block is one the game can list: an
    // unsupported COURSE, a repeated difficulty and side, or a #START player other than P1/P2
    // make SongInfo.Read throw, for the game and the servers alike.
    public sealed class TjaBlock
    {
        public int Line;              // 1-based line of the #START
        public string Course = "";    // COURSE as written ("Oni", "3", "Ura"), passed back to TjaParser.Parse
        public Difficulty Difficulty;
        public string Side = "single", Player = ""; // ChartSide.OfStart(Player); Player as after #START, upper-case
        public string Style = "";     // STYLE in force, as written; reset by each COURSE
        public int Level;
        public bool IsBranching, HasNotes;
        // NOTESDESIGNER0–4 credits one course each, else MAKER (the Database's rule).
        public string Maker = "";
    }

    // What a TJA file says about itself: the header and every block. Which block a player plays
    // is the game's choice (OurTaikoPlay SeatBlocks); the servers register Blocks as they are.
    public sealed class SongInfo
    {
        // TITLE*/SUBTITLE* as written: "origin" is TITLE/SUBTITLE itself, the other keys are the
        // locales en, ja, ko, zh, zh_tw. Servers store and return the whole dictionary; a client
        // picks by language (DisplayTitle / DisplaySubtitle).
        public readonly Dictionary<string, string> Titles = new Dictionary<string, string>();
        public readonly Dictionary<string, string> Subtitles = new Dictionary<string, string>();
        // The language Read was asked for; Title and Subtitle are its display forms.
        public string Language;
        public string Title => DisplayTitle(Language);
        public string Subtitle => DisplaySubtitle(Language);
        public string Genre = "", Wave = "";
        // MAKER header: the chart author, shown in turn with the subtitle while playing.
        public string Maker = "";
        public double Bpm = 120, DemoStart, Offset;
        // BGMOVIE and MOVIEOFFSET headers, as written / in seconds.
        public string Movie = "";
        public double MovieOffset;
        // NOTESDESIGNER0–4 by course name (Easy…Edit).
        public readonly Dictionary<string, string> NotesDesigners = new Dictionary<string, string>();
        // Every #START block in file order.
        public readonly List<TjaBlock> Blocks = new List<TjaBlock>();

        // The title in a language: its translation when there is one, else TITLE itself.
        public string DisplayTitle(string language) => Pick(Titles, language) ?? "Untitled";
        // The subtitle likewise, without the leading -- or ++ that hides or shows it in song select.
        public string DisplaySubtitle(string language) => (Pick(Subtitles, language) ?? "").TrimStart('-', '+');

        static string Pick(Dictionary<string, string> texts, string language)
        {
            if (language != null && texts.TryGetValue(language, out string text) && !string.IsNullOrWhiteSpace(text)) return text;
            return texts.TryGetValue("origin", out text) ? text : null;
        }

        // TJA COURSE names of Easy..Ura, as charts, scores and servers spell them.
        public static readonly System.Collections.ObjectModel.ReadOnlyCollection<string> CourseNames =
            Array.AsReadOnly(new[] { "Easy", "Normal", "Hard", "Oni", "Edit" });

        public static string CourseName(Difficulty difficulty) => CourseNames[(int)difficulty];

        public static Difficulty? DifficultyOf(string course)
        {
            string value = course.Trim().Split('_')[0];
            if (int.TryParse(value, out int n)) return n >= 0 && n < CourseNames.Count ? (Difficulty)n : (Difficulty?)null;
            if (value.Equals("Ura", StringComparison.OrdinalIgnoreCase)) return Difficulty.Ura;
            for (int i = 0; i < CourseNames.Count; i++)
                if (value.Equals(CourseNames[i], StringComparison.OrdinalIgnoreCase)) return (Difficulty)i;
            return null;
        }

        // The block of a difficulty on a side, or null.
        public TjaBlock Block(Difficulty difficulty, string side) => Blocks.Find(b => b.Difficulty == difficulty && b.Side == side);

        public static SongInfo Read(string text, string language = null)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("The TJA chart is empty.");
            var lines = TjaLines.Read(text).ToList();
            // Whole-file checks first: an unsupported course anywhere stops the read.
            foreach (var line in lines)
                if (line.Kind == TjaLineKind.Header && line.Key == "COURSE" && DifficultyOf(line.Value) == null)
                    throw new FormatException($"Unsupported COURSE '{line.Value}' (line {line.Line}).");
            var info = new SongInfo { Language = language };
            string course = "Oni", style = "";
            int level = 0;
            TjaBlock block = null;
            foreach (var line in lines)
            {
                if (line.Kind == TjaLineKind.Start)
                {
                    if (line.Value != "" && line.Value != "P1" && line.Value != "P2")
                        throw new FormatException($"#START takes P1 or P2, not '{line.Value}' (line {line.Line}).");
                    var difficulty = DifficultyOf(course).Value;
                    string side = ChartSide.OfStart(line.Value);
                    var twice = info.Blocks.Find(b => b.Difficulty == difficulty && b.Side == side);
                    if (twice != null)
                        throw new FormatException($"{CourseName(difficulty)}/{side} is written twice (lines {twice.Line} and {line.Line}).");
                    block = new TjaBlock { Line = line.Line, Course = course, Difficulty = difficulty, Side = side, Player = line.Value, Style = style, Level = level };
                    info.Blocks.Add(block);
                    continue;
                }
                if (line.Kind == TjaLineKind.End) { block = null; continue; }
                if (line.Kind == TjaLineKind.Body)
                {
                    if (line.Text.StartsWith("#BRANCHSTART", StringComparison.OrdinalIgnoreCase)) block.IsBranching = true;
                    else if (line.Text[0] != '#' && line.Text.Any(char.IsDigit)) block.HasNotes = true;
                    continue;
                }
                if (line.Kind != TjaLineKind.Header) continue;
                string key = line.Key, value = line.Value;
                switch (key)
                {
                    case "TITLE": info.Titles["origin"] = value; break;
                    case "SUBTITLE": info.Subtitles["origin"] = value; break;
                    case "GENRE": info.Genre = value; break;
                    case "MAKER": info.Maker = value.Trim(); break;
                    case "WAVE": info.Wave = value; break;
                    case "BGMOVIE": info.Movie = value; break;
                    case "MOVIEOFFSET": info.MovieOffset = Number(value, 0); break;
                    case "BPM": info.Bpm = Number(value, info.Bpm); break;
                    case "OFFSET": info.Offset = Number(value, 0); break;
                    case "DEMOSTART": info.DemoStart = Number(value, 0); break;
                    case "COURSE": course = value; level = 0; style = ""; break;
                    case "LEVEL": level = (int)Number(value, 0); break;
                    case "STYLE": style = value; break;
                    case "NOTESDESIGNER0": case "NOTESDESIGNER1": case "NOTESDESIGNER2": case "NOTESDESIGNER3": case "NOTESDESIGNER4":
                        info.NotesDesigners[CourseNames[key[key.Length - 1] - '0']] = value.Trim();
                        break;
                    default:
                        bool subtitle = key.StartsWith("SUBTITLE", StringComparison.Ordinal);
                        string prefix = subtitle ? "SUBTITLE" : "TITLE";
                        if (!key.StartsWith(prefix, StringComparison.Ordinal)) break;
                        string locale = TitleLanguage(key.Substring(prefix.Length));
                        if (locale != null) (subtitle ? info.Subtitles : info.Titles)[locale] = value;
                        break;
                }
            }
            foreach (var b in info.Blocks)
                b.Maker = info.NotesDesigners.TryGetValue(CourseName(b.Difficulty), out string designer) && designer.Length > 0 ? designer : info.Maker;
            return info;
        }

        static string TitleLanguage(string suffix) => suffix switch
        {
            "EN" => "en", "JA" or "JP" => "ja", "KO" => "ko",
            "ZH" or "CN" or "ZH_CN" or "ZH-CN" => "zh",
            "TW" or "ZH_TW" or "ZH-TW" => "zh_tw",
            _ => null,
        };

        static double Number(string value, double fallback) => TjaNumber.TryParse(value, out double result) ? result : fallback;
    }
}
