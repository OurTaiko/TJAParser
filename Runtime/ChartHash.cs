using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OurTaiko
{
    // One block of a TJA file with its content hash. Course and Side key the block the way
    // SongInfo lists it and the servers register it (Easy/Normal/Hard/Oni/Edit; single/p1/p2).
    // A block TjaParser cannot read has no Hash and carries the parser's message in Error.
    public sealed class ChartBlockHash
    {
        public Difficulty Difficulty;
        public string Course, Side;
        public int Level;
        public bool IsBranching;
        public string Hash, Error;
        // The block as TjaParser read it (null when it could not), for callers that read more
        // from it than the hash.
        public TaikoChart Chart;
        // TjaParser's warnings for the block (OpenTaiko tolerances it applied).
        public readonly List<string> Warnings = new List<string>();
        public bool Ok => Hash != null;
    }

    // The content hash of a chart block: "v2:" + SHA-256 (hex) of ChartCanonical.Write over the
    // chart TjaParser produces. Two files whose block plays the same get the same hash, whatever
    // their titles, comments or spelling; the servers use it to tell whether scores, dan courses
    // and local records still belong to the current chart (OurTaikoLogs/TJAParser).
    public static class ChartHash
    {
        public const int Version = ChartCanonical.Version;
        public static readonly string Prefix = "v" + Version + ":";

        public static string Of(TaikoChart chart) => OfCanonical(ChartCanonical.Write(chart));

        // SHA-256 of the text's UTF-8 bytes, encoded a buffer at a time rather than all at once.
        public static string OfCanonical(string canonical)
        {
            byte[] sum;
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var encoder = Encoding.UTF8.GetEncoder();
                var buffer = new byte[8192];
                var chars = canonical.AsSpan();
                bool completed = false;
                while (!completed)
                {
                    encoder.Convert(chars, buffer, true, out int used, out int written, out completed);
                    sha.AppendData(buffer, 0, written);
                    chars = chars.Slice(used);
                }
                sum = sha.GetHashAndReset();
            }
            var hex = new char[Prefix.Length + sum.Length * 2];
            Prefix.CopyTo(0, hex, 0, Prefix.Length);
            for (int i = 0; i < sum.Length; i++)
            {
                hex[Prefix.Length + 2 * i] = HexDigits[sum[i] >> 4];
                hex[Prefix.Length + 2 * i + 1] = HexDigits[sum[i] & 15];
            }
            return new string(hex);
        }

        const string HexDigits = "0123456789abcdef";

        // Every block the game can list (SongInfo.Blocks: every #START, in file order; a file with
        // a difficulty and side written twice is refused), each parsed as the game plays it.
        // The file's lines are read once, for SongInfo and every block.
        public static List<ChartBlockHash> OfFile(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("The TJA chart is empty.");
            var lines = TjaLines.Read(text).ToList();
            return OfFile(text, lines, SongInfo.Read(lines, null));
        }

        public static List<ChartBlockHash> OfFile(string text, SongInfo info) => OfFile(text, TjaLines.Read(text ?? "").ToList(), info);

        static List<ChartBlockHash> OfFile(string text, List<TjaLine> lines, SongInfo info)
        {
            var result = new List<ChartBlockHash>(info.Blocks.Count);
            foreach (var block in info.Blocks) result.Add(OfBlock(text, lines, block));
            return result;
        }

        // One listed block, parsed as the game plays it.
        public static ChartBlockHash OfBlock(string text, TjaBlock block) => OfBlock(text, null, block);

        static ChartBlockHash OfBlock(string text, List<TjaLine> lines, TjaBlock block)
        {
            var entry = new ChartBlockHash
            {
                Difficulty = block.Difficulty, Course = SongInfo.CourseName(block.Difficulty), Side = block.Side,
                Level = block.Level, IsBranching = block.IsBranching,
            };
            try
            {
                var chart = lines == null ? TjaParser.Parse(text, block.Course, block.Side)
                    : TjaParser.Parse(text, lines, block.Course, block.Side);
                entry.Hash = Of(chart);
                entry.Chart = chart;
                entry.Warnings.AddRange(chart.Warnings);
            }
            catch (Exception e) { entry.Error = e.Message; }
            return entry;
        }
    }
}
