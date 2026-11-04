using System.Collections.Generic;

namespace OurTaiko
{
    // What an editor reads of one block of a TJA file (TjaParser.Analyze): the chart, every problem
    // where it is, and (from later steps) where each part of the chart comes from. The game and the
    // servers use TjaParser.Parse, which tracks none of this.
    public sealed class TjaAnalysis
    {
        public string Text { get; }
        // The block asked for, as in TjaParser.Parse.
        public string Course { get; }
        public string Side { get; }
        // The chart as read, errors recovered from (see TjaParser.Analyze); null when there is no
        // such block, or no text.
        public TaikoChart Chart { get; internal set; }
        public IReadOnlyList<TjaDiagnostic> Diagnostics => diagnostics;
        public bool HasErrors => diagnostics.Exists(d => d.Severity == TjaSeverity.Error);

        // Where each note, bar line and event of Chart is written.
        public ChartSourceMap SourceMap { get; } = new ChartSourceMap();

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

        internal readonly List<TjaDiagnostic> diagnostics = new List<TjaDiagnostic>();

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
}
