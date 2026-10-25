using System;
using System.Collections.Generic;
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

    // The content hash of a chart block: "v1:" + SHA-256 (hex) of ChartCanonical.Write over the
    // chart TjaParser produces. Two files whose block plays the same get the same hash, whatever
    // their titles, comments or spelling; the servers use it to tell whether scores, dan courses
    // and local records still belong to the current chart (OurTaikoLogs/TJAParser).
    public static class ChartHash
    {
        public const int Version = ChartCanonical.Version;
        public static readonly string Prefix = "v" + Version + ":";

        public static string Of(TaikoChart chart) => OfCanonical(ChartCanonical.Write(chart));

        public static string OfCanonical(string canonical)
        {
            using var sha = SHA256.Create();
            byte[] sum = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
            var sb = new StringBuilder(Prefix, Prefix.Length + sum.Length * 2);
            foreach (byte b in sum) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        // Every block the game can list (SongInfo.Blocks: the first block of each difficulty on
        // each side, in that order), each parsed as the game plays it.
        public static List<ChartBlockHash> OfFile(string text) => OfFile(text, SongInfo.Read(text));

        public static List<ChartBlockHash> OfFile(string text, SongInfo info)
        {
            var result = new List<ChartBlockHash>();
            foreach (var block in info.Blocks) result.Add(OfBlock(text, block));
            return result;
        }

        // One listed block, parsed as the game plays it.
        public static ChartBlockHash OfBlock(string text, TjaBlock block)
        {
            var entry = new ChartBlockHash
            {
                Difficulty = block.Difficulty, Course = SongInfo.CourseName(block.Difficulty), Side = block.Side,
                Level = block.Level, IsBranching = block.IsBranching,
            };
            try
            {
                var chart = TjaParser.Parse(text, block.Course, block.Side);
                entry.Hash = Of(chart);
                entry.Chart = chart;
                entry.Warnings.AddRange(chart.Warnings);
            }
            catch (Exception e) { entry.Error = e.Message; }
            return entry;
        }
    }
}
