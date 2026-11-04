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

        TjaSourceText lines;
        // Lines and columns of Text.
        public TjaSourceText Lines => lines ?? (lines = new TjaSourceText(Text));

        internal readonly List<TjaDiagnostic> diagnostics = new List<TjaDiagnostic>();

        internal TjaAnalysis(string text, string course, string side)
        {
            Text = text; Course = course; Side = side;
        }
    }
}
