using System.Globalization;
using System.Text;

namespace OurTaiko.Corpus;

// Synthetic TJA files for the benchmarks, the golden differential test and the fuzz test. The
// same seed always gives the same text (System.Random with a seed is stable across runtimes), so
// the golden digests stay valid; changing this generator means regenerating them.
public static class ChartCorpus
{
    static readonly string[] CourseNames = { "Easy", "Normal", "Hard", "Oni", "Edit" };

    // A whole file: headers, then 1–5 courses (and sometimes an Oni P1/P2 pair), each with
    // `measures` measures. `messy` adds what real charts get wrong: stray 8s, unended rolls,
    // unknown or unreadable commands, branches without #N, a missing #END, comments.
    public static string Generate(int seed, int measures, bool messy, int courses = 0)
    {
        var r = new Random(seed);
        var sb = new StringBuilder();
        sb.Append("TITLE:Corpus ").Append(seed).Append('\n');
        if (r.Next(2) == 0) sb.Append("TITLEJA:コーパス ").Append(seed).Append('\n');
        sb.Append("SUBTITLE:--generated\n");
        sb.Append("BPM:").Append(Pick(r, "120", "150.5", "200", "90,5", "180,", "222")).Append('\n');
        sb.Append("OFFSET:").Append(F(-Math.Round(r.NextDouble() * 3, 3))).Append('\n');
        sb.Append("WAVE:song.ogg\n");
        if (r.Next(3) == 0) sb.Append("BALLOON:5,10,15\n");
        if (messy && r.Next(4) == 0) sb.Append("// a comment before the courses\n");

        var order = Enumerable.Range(0, 5).OrderBy(_ => r.Next()).ToList();
        int count = courses > 0 ? Math.Min(courses, 5) : 1 + r.Next(5);
        foreach (int d in order.Take(count))
        {
            string course = r.Next(4) switch
            {
                0 => d.ToString(CultureInfo.InvariantCulture),
                1 when d == 4 => "Ura",
                _ => CourseNames[d],
            };
            bool pair = d == 3 && r.Next(3) == 0;
            sb.Append('\n').Append("COURSE:").Append(course).Append('\n');
            sb.Append("LEVEL:").Append(1 + r.Next(10)).Append('\n');
            if (r.Next(2) == 0) sb.Append(Pick(r, "BALLOON:", "BALLOONNOR:", "BALLOONEXP:", "BALLOONMAS:"))
                .Append(string.Join(",", Enumerable.Range(0, 1 + r.Next(8)).Select(_ => 3 + r.Next(40)))).Append('\n');
            if (r.Next(5) == 0) sb.Append(Pick(r, "#BMSCROLL", "#HBSCROLL", "#NMSCROLL")).Append('\n');
            if (pair)
            {
                sb.Append("STYLE:Double\n");
                Block(sb, r, measures, messy, "#START P1");
                Block(sb, r, measures, messy, "#START P2");
            }
            else Block(sb, r, measures, messy, "#START");
        }
        return sb.ToString();
    }

    static void Block(StringBuilder sb, Random r, int measures, bool messy, string start)
    {
        sb.Append(start).Append('\n');
        int m = 0;
        while (m < measures)
        {
            if (r.Next(100) < 5 && measures - m > 6) { m += Branch(sb, r, messy); continue; }
            Commands(sb, r, messy);
            Measure(sb, r, messy, closeRolls: !messy || r.Next(4) > 0);
            m++;
        }
        if (messy && r.Next(10) == 0) sb.Append("1010\n"); // a last measure without its comma
        if (!messy || r.Next(12) > 0) sb.Append("#END\n");
    }

    static int Branch(StringBuilder sb, Random r, bool messy)
    {
        sb.Append("#BRANCHSTART ").Append(Pick(r, "p,50,80", "p, 70, 90", "r,10,20", "s,500000,800000", "pp,60,85",
            "jp,100,200", "d,10,20", "R,5,10", "rb,5,10", "p,50,80,l", messy ? "x,1,2" : "jg,1,2")).Append('\n');
        int length = 1 + r.Next(4), used = 0;
        if (messy && r.Next(6) == 0) { Measure(sb, r, messy, true); used++; } // common notes before the first route
        foreach (string route in new[] { "#N", "#E", "#M" })
        {
            if (route == "#N" && messy && r.Next(8) == 0) continue;
            if (route != "#N" && r.Next(6) == 0) continue;
            sb.Append(route).Append('\n');
            if (r.Next(10) == 0) sb.Append("#LEVELHOLD\n");
            for (int i = 0; i < length; i++)
            {
                Commands(sb, r, messy);
                Measure(sb, r, messy, true);
            }
        }
        if (!messy || r.Next(4) > 0) sb.Append("#BRANCHEND\n");
        return used + length;
    }

    static void Commands(StringBuilder sb, Random r, bool messy)
    {
        int n = r.Next(10) < 6 ? 0 : 1 + r.Next(2);
        for (int i = 0; i < n; i++) sb.Append(Command(r, messy)).Append('\n');
    }

    static string Command(Random r, bool messy)
    {
        switch (r.Next(messy ? 18 : 15))
        {
            case 0: return "#BPMCHANGE " + Pick(r, "60", "120", "150.5", "200", "240,", "333.33");
            case 1: return "#MEASURE " + Pick(r, "4/4", "3/4", "7/8", "5/4", "2/4", "6/8");
            case 2: return "#SCROLL " + Pick(r, "0.5", "1", "1.5", "2", "-1", "1+1i", "2i", "0.75 0.5i", "1.33");
            case 3: return "#DELAY " + Pick(r, "0.1", "0.25", "0.5", "-0.1");
            case 4: return r.Next(2) == 0 ? "#GOGOSTART" : "#GOGOEND";
            case 5: return r.Next(2) == 0 ? "#BARLINEOFF" : "#BARLINEON";
            case 6: return "#SECTION";
            case 7: return Pick(r, "#NMSCROLL", "#BMSCROLL", "#HBSCROLL");
            case 8: return "#JPOSSCROLL " + Pick(r, "0.5 100 1", "1 -50+20i", "0 30", "2 200 0");
            case 9: return "#SUDDEN " + Pick(r, "2 1", "0 0", "3 0", "1.5 0.5");
            case 10: return "#LANE_ALPHA " + Pick(r, "50 500", "100% -200", "0", "25 0");
            case 11: return "#LEVELHOLD";
            case 12: return "#scroll " + Pick(r, "1", "0.8"); // commands are read in any case
            case 13: return "#SCROLL " + Pick(r, "1", "0.9");
            case 14: return "#BPMCHANGE " + Pick(r, "130", "175");
            case 15: return Pick(r, "#SCROLL abc", "#DELAY x", "#LANE_ALPHA 150", "#SUDDEN 1", "#JPOSSCROLL x");
            case 16: return Pick(r, "#UNKNOWNCMD", "#DIRECTION 1", "#BGAON 1");
            default: return Pick(r, "// a comment line", "/* a block comment */", "/* across\nlines */");
        }
    }

    static readonly int[] Slots = { 0, 1, 4, 8, 12, 16, 16, 16, 24, 32, 48 };

    static void Measure(StringBuilder sb, Random r, bool messy, bool closeRolls)
    {
        int slots = Slots[r.Next(Slots.Length)];
        var notes = new StringBuilder(slots + 8);
        for (int i = 0; i < slots; i++)
        {
            int k = r.Next(100);
            if (k < 45) notes.Append('0');
            else if (k < 70) notes.Append('1');
            else if (k < 88) notes.Append('2');
            else if (k < 92) notes.Append('3');
            else if (k < 95) notes.Append('4');
            else if (k < 98 && i + 2 < slots)
            {
                char head = Pick(r, '5', '6', '7', '9');
                notes.Append(head);
                if (head is '7' or '9' && r.Next(4) == 0) notes.Append('[').Append(2 + r.Next(30)).Append(']');
                int gap = 1 + r.Next(Math.Min(4, slots - i - 2));
                notes.Append('0', gap - 1);
                if (closeRolls) notes.Append('8');
                else notes.Append('0');
                i += gap;
            }
            else if (messy && k == 98) notes.Append(Pick(r, '8', 'E')); // a stray 8, the unused E
            else notes.Append(Pick(r, 'G', 'C', 'F'));
        }
        // Split the measure over a few lines, sometimes with a command between them.
        string text = notes.ToString();
        int lines = text.Length < 8 ? 1 : 1 + r.Next(3);
        int at = 0;
        for (int l = 0; l < lines; l++)
        {
            int end = l == lines - 1 ? text.Length : at + (text.Length - at) / (lines - l);
            int open = end < text.Length ? text.LastIndexOf('[', end) : -1;
            if (open >= at && text.IndexOf(']', open) >= end) end = text.IndexOf(']', open) + 1;
            if (end <= at) continue;
            sb.Append(text, at, end - at);
            if (l == lines - 1) sb.Append(',');
            if (messy && r.Next(30) == 0) sb.Append(" // trailing");
            sb.Append('\n');
            at = end;
            if (l < lines - 1 && r.Next(3) == 0) sb.Append(Command(r, false)).Append('\n');
        }
        if (text.Length == 0) sb.Append(",\n");
    }

    // A few random edits of a file, for fuzzing: lines deleted, repeated, swapped, overwritten
    // with an edge-case command or header, characters changed, or the file cut short.
    public static string Mutate(string text, int seed)
    {
        var r = new Random(seed);
        var lines = text.Split('\n').ToList();
        int edits = 1 + r.Next(4);
        for (int e = 0; e < edits && lines.Count > 0; e++)
        {
            int i = r.Next(lines.Count);
            switch (r.Next(7))
            {
                case 0: lines.RemoveAt(i); break;
                case 1: lines.Insert(i, lines[i]); break;
                case 2: { int j = r.Next(lines.Count); (lines[i], lines[j]) = (lines[j], lines[i]); break; }
                case 3: lines.Insert(i, Pick(r, EdgeLines)); break;
                case 4:
                {
                    var chars = lines[i].ToCharArray();
                    if (chars.Length == 0) break;
                    chars[r.Next(chars.Length)] = MutationChars[r.Next(MutationChars.Length)];
                    lines[i] = new string(chars);
                    break;
                }
                case 5: lines[i] += Pick(r, "[", "]", "[5]", "[0]", "/*", "*/", " ", ",", "8", "E"); break;
                default: lines.RemoveRange(i, lines.Count - i); break;
            }
        }
        return string.Join("\n", lines);
    }

    static readonly string MutationChars = "0123456789,#[]/*ABCEFGabce :-.i\t";

    static readonly string[] EdgeLines =
    {
        "#MEASURE 4", "#MEASURE 0/4", "#MEASURE 4/0", "#MEASURE /", "#MEASURE", "#BPMCHANGE 0", "#BPMCHANGE -1", "#BPMCHANGE",
        "#BPMCHANGE x", "#SCROLL", "#SCROLL 1+i+i", "#SCROLL i", "#DELAY", "#DELAY inf", "#BRANCHSTART", "#BRANCHSTART p",
        "#BRANCHSTART p,1", "#N", "#E", "#M", "#BRANCHEND", "#END", "#START", "#START P3", "#START P1", "#LEVELHOLD",
        "#JPOSSCROLL", "#JPOSSCROLL 1 1 x", "#SUDDEN", "#LANE_ALPHA", "#LANE_ALPHA 50 x", "#LANE_ALPHA -1",
        "COURSE:Oni", "COURSE:9", "COURSE:Ura", "COURSE:", "LEVEL:x", "LEVEL:", "BPM:x", "BPM:0", "BPM:", "OFFSET:x",
        "BALLOON:1,x", "BALLOON:", "BALLOON:,5", "TITLE:", "STYLE:Double", "7[", "7[x]", "7[0]", "[5]", "1[5]", "9[ 3 ]8,",
        "/*", "*/", "a", "1a", "H", ",,,", "8", "5", "7", "G", "C", "F", "E", "e", "\r", "﻿1,",
    };

    static T Pick<T>(Random r, params T[] items) => items[r.Next(items.Length)];

    static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}
