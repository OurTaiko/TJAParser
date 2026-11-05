using System;
using System.Collections.Generic;
using System.Threading;

namespace OurTaiko
{
    // What an editor reads of one block of a TJA file (TjaParser.Analyze): the chart, every problem
    // where it is, and where each part of the chart comes from. The game and the servers use
    // TjaParser.Parse, which tracks none of this. An analysis is a snapshot: nothing in it changes
    // once it is made (Chart included: it is not to be changed by its readers either), so threads
    // may read it at once.
    public sealed class TjaAnalysis
    {
        public string Text { get; }
        // The block asked for, as in TjaParser.Parse.
        public string Course { get; }
        public string Side { get; }
        // The TjaDocument version it is of (0 when made by TjaParser.Analyze).
        public int Version { get; internal set; }
        // The chart as read, errors recovered from (see TjaParser.Analyze); null when there is no
        // such block, or no text.
        public TaikoChart Chart { get; internal set; }
        public IReadOnlyList<TjaDiagnostic> Diagnostics => diagnostics;
        public bool HasErrors => diagnostics.Exists(d => d.Severity == TjaSeverity.Error);

        // Where each note, bar line and event of Chart is written.
        public ChartSourceMap SourceMap { get; internal set; } = new ChartSourceMap();

        // The block's #START line, and where it ends (its #END, or where a missing #END would be).
        public TextSpan StartSpan { get; internal set; }
        public TextSpan EndSpan { get; internal set; }

        // What is in force at a position of the block's body (from the end of #START to the start of
        // #END): what the next token there is read with. Null outside the body or without a chart.
        // The block is read again up to there, so a call costs about one parse of the block.
        public TjaPositionState StateAt(int offset, CancellationToken cancel = default)
        {
            if (Chart == null || offset < StartSpan.End || offset > EndSpan.Start) return null;
            var sources = new List<LineSource>();
            var parser = new TjaParser.ChartParser(sources, new List<TjaDiagnostic>()) { Cancel = cancel };
            return parser.Capture(Text, TjaLines.Read(Text, sources), Course, Side, offset);
        }

        // The note nearest a time that a player on a route hits (common notes, and in each branch
        // the route drawn for it), for a preview to find in the text; the first of notes as near.
        // Null without a chart or a note.
        public ChartNote NoteNear(double time, BranchRoute route = BranchRoute.Normal)
        {
            if (Chart == null) return null;
            ChartNote best = null;
            double distance = double.PositiveInfinity;
            foreach (var note in Chart.Notes)
            {
                if (note.BranchId >= 0 && note.Route != Chart.Branches[note.BranchId].ResolveRoute(route)) continue;
                double d = Math.Abs(note.Time - time);
                if (d < distance) { best = note; distance = d; }
            }
            return best;
        }

        // Every measure in the order it is written: the common ones and each route's (see
        // Played for one route's run through the chart).
        public IReadOnlyList<ChartMeasure> Measures => measures;
        internal readonly List<ChartMeasure> measures = new List<ChartMeasure>();

        // The measures a player on a route goes through, in time order: the common ones and, in
        // each branch, the route drawn for it (ChartBranch.ResolveRoute: a missing #E plays #N).
        public List<ChartMeasure> Played(BranchRoute route)
        {
            var played = new List<ChartMeasure>();
            foreach (var m in measures)
                if (m.BranchId < 0 || m.Route == Chart.Branches[m.BranchId].ResolveRoute(route)) played.Add(m);
            // Stable: measures at one time keep their written order.
            for (int i = 1; i < played.Count; i++)
                for (int k = i; k > 0 && played[k - 1].StartTime > played[k].StartTime; k--)
                    (played[k - 1], played[k]) = (played[k], played[k - 1]);
            return played;
        }

        TjaSourceText lines;
        // Lines and columns of Text.
        public TjaSourceText Lines => lines ?? (lines = new TjaSourceText(Text));

        // A document's line table, shared by its blocks' analyses.
        internal void Share(TjaSourceText table)
        {
            if (lines == null) lines = table;
        }

        internal readonly List<TjaDiagnostic> diagnostics = new List<TjaDiagnostic>();
        // The lines read, up to the block's end, and where its #START is among them (TjaDocument).
        internal List<TjaLine> ReadLines;
        internal int StartLineIndex = -1;

        internal TjaAnalysis(string text, string course, string side)
        {
            Text = text; Course = course; Side = side;
        }
    }

    // One measure of an analysed block: its number as the warnings count them (in written order,
    // across routes), where it is written (first note to comma), when it starts and ends, the BPM
    // and length (in whole notes: 1 is 4/4) it starts with, its note slots, and its bar line.
    public sealed class ChartMeasure
    {
        public int Index { get; internal set; }
        public TextSpan Span { get; internal set; }
        public double StartTime { get; internal set; }
        public double EndTime { get; internal set; }
        public double Bpm { get; internal set; }
        public double Length { get; internal set; }
        public int Slots { get; internal set; }
        public int BranchId { get; internal set; } = -1;
        public BranchRoute Route { get; internal set; }
        public ChartNote Bar { get; internal set; }
    }

    // What is in force at a position of a block (TjaAnalysis.StateAt). Time, Beat, Bpm, ScrollX,
    // ScrollY, ScrollMode, SuddenShow and SuddenMove are what a note written there would get.
    public sealed class TjaPositionState
    {
        public double Time { get; internal set; }
        public double Beat { get; internal set; }
        public double Bpm { get; internal set; }
        // The measure's length in whole notes (#MEASURE 3/4 is 0.75).
        public double MeasureLength { get; internal set; }
        public double ScrollX { get; internal set; }
        public double ScrollY { get; internal set; }
        public ScrollMode ScrollMode { get; internal set; }
        public bool Barline { get; internal set; }
        public bool Gogo { get; internal set; }
        public double SuddenShow { get; internal set; }
        public double SuddenMove { get; internal set; }
        // The measure being written (its Index), the note slot in it, and how many it has (0 and 0
        // between measures, as before a branch command).
        public int Measure { get; internal set; }
        public int Slot { get; internal set; }
        public int Slots { get; internal set; }
        // The branch being written (-1 outside one), and its route (null before the first #N/#E/#M,
        // where notes are common, and outside a branch).
        public int BranchId { get; internal set; }
        public BranchRoute? Route { get; internal set; }
        // The kind of the roll still open on the route being written, if any.
        public NoteKind? OpenRoll { get; internal set; }
        // The list the next balloon takes its count from, its place there, and the count: from the
        // list (BalloonListed), or 5 past its end.
        public string BalloonList { get; internal set; }
        public int BalloonIndex { get; internal set; }
        public int NextBalloonHits { get; internal set; }
        public bool BalloonListed { get; internal set; }
    }
}
