using System;
using System.Collections.Generic;
using System.Linq;

namespace OurTaiko
{
    // Difficulty columns of the original song select (src/libs/global_data.h Difficulty);
    // Edit is the Ura course that shares the Oni column.
    public enum Difficulty { Back = -3, Modifier = -2, Neiro = -1, Easy = 0, Normal, Hard, Oni, Ura }

    public sealed class CourseInfo
    {
        public Difficulty Difficulty;
        public string Course; // The TJA's own COURSE value, passed back to TjaParser.Parse.
        public string Side;   // The block's side ("single", "p1", "p2"), passed back to TjaParser.Parse with Course.
        public int Level;
        public bool IsBranching;
    }

    // Song-select metadata of a TJA file: the header, every block, and every course with a block
    // the seat plays.
    public sealed class SongInfo
    {
        public string Title = "Untitled", Subtitle = "", Genre = "", Wave = "";
        // MAKER header: the chart author, shown in turn with the subtitle while playing.
        public string Maker = "";
        public double Bpm = 120, DemoStart;
        public readonly List<CourseInfo> Courses = new List<CourseInfo>();
        // The first block of each difficulty on each side, by difficulty and then side (ChartSide.All).
        public readonly List<CourseInfo> Blocks = new List<CourseInfo>();

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

        public CourseInfo Course(Difficulty difficulty) => Courses.Find(c => c.Difficulty == difficulty);
        public bool Has(Difficulty difficulty) => Course(difficulty) != null;

        // The block a seat ("p1" or "p2") plays on a difficulty (ChartSide); null when it has none.
        // Unlike Course, any seat may be asked whichever seat the info was read for.
        public CourseInfo Block(Difficulty difficulty, string seat = "p1")
        {
            foreach (string side in ChartSide.Reads(seat))
            {
                var block = Blocks.Find(c => c.Difficulty == difficulty && c.Side == side);
                if (block != null) return block;
            }
            return null;
        }

        public static SongInfo Read(string text, string language = null, string seat = "p1")
        {
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("The TJA chart is empty.");
            var info = new SongInfo();
            var titles = new Dictionary<string, string>();
            var subtitles = new Dictionary<string, string>();
            string course = "Oni";
            int level = 0;
            CourseInfo reading = null;
            // The first block of each difficulty on each side; the seat then picks the side it plays.
            foreach (var line in TjaLines.Read(text))
            {
                if (line.Kind == TjaLineKind.Start)
                {
                    var difficulty = DifficultyOf(course);
                    reading = difficulty == null ? null
                        : new CourseInfo { Difficulty = difficulty.Value, Course = course, Side = ChartSide.OfStart(line.Value), Level = level };
                    continue;
                }
                if (line.Kind == TjaLineKind.End)
                {
                    if (reading != null && !info.Blocks.Exists(c => c.Difficulty == reading.Difficulty && c.Side == reading.Side))
                        info.Blocks.Add(reading);
                    reading = null;
                    continue;
                }
                if (line.Kind == TjaLineKind.Body)
                {
                    if (reading != null && line.Text.StartsWith("#BRANCHSTART", StringComparison.OrdinalIgnoreCase)) reading.IsBranching = true;
                    continue;
                }
                if (line.Kind != TjaLineKind.Header) continue;
                string key = line.Key, value = line.Value;
                switch (key)
                {
                    case "TITLE": info.Title = value; titles["en"] = value; break;
                    case "SUBTITLE": info.Subtitle = value.TrimStart('-', '+'); subtitles["en"] = info.Subtitle; break;
                    case "GENRE": info.Genre = value; break;
                    case "MAKER": info.Maker = value.Trim(); break;
                    case "WAVE": info.Wave = value; break;
                    case "BPM": info.Bpm = Number(value, info.Bpm); break;
                    case "DEMOSTART": info.DemoStart = Number(value, 0); break;
                    case "COURSE": course = value; level = 0; break;
                    case "LEVEL": level = (int)Number(value, 0); break;
                    default:
                        bool subtitle = key.StartsWith("SUBTITLE", StringComparison.Ordinal);
                        string prefix = subtitle ? "SUBTITLE" : "TITLE";
                        if (!key.StartsWith(prefix, StringComparison.Ordinal)) break;
                        string locale = TitleLanguage(key.Substring(prefix.Length));
                        if (locale != null) (subtitle ? subtitles : titles)[locale] = subtitle ? value.TrimStart('-', '+') : value;
                        break;
                }
            }
            if (language != null)
            {
                info.Title = Translated(titles, language, info.Title);
                info.Subtitle = Translated(subtitles, language, info.Subtitle);
            }
            info.Blocks.Sort((a, b) => a.Difficulty != b.Difficulty ? a.Difficulty.CompareTo(b.Difficulty)
                : Array.IndexOf(ChartSide.All, a.Side).CompareTo(Array.IndexOf(ChartSide.All, b.Side)));
            foreach (var difficulty in info.Blocks.Select(c => c.Difficulty).Distinct())
                if (info.Block(difficulty, seat) is CourseInfo block) info.Courses.Add(block);
            return info;
        }

        // Display names: the chosen language's translation, else the chart's original TITLE/SUBTITLE.
        // Online metadata uses the same rule.
        internal static string Translated(Dictionary<string, string> values, string language, string original)
            => values.TryGetValue(language, out string text) && !string.IsNullOrWhiteSpace(text) ? text : original;

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
