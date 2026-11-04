using System.Collections.Generic;

namespace OurTaiko
{
    // Where the parts of an analysed chart come from in its text (TjaAnalysis.SourceMap): a note
    // its character (a balloon with its inline count), a roll's end the 8 (or whatever ended it),
    // a bar line its measure (first note to comma), and an event its command line (ChartTempo,
    // ChartGogo, ChartSection, ChartJudgeMove, ChartLaneAlpha, ChartBranch). Not part of the chart
    // the game plays or of its hash.
    public sealed class ChartSourceMap
    {
        readonly Dictionary<object, TextSpan> spans = new Dictionary<object, TextSpan>();
        readonly Dictionary<ChartNote, TextSpan> tails = new Dictionary<ChartNote, TextSpan>();
        // Every note head and tail by where it is written, for NoteAt.
        readonly List<(TextSpan Span, ChartNote Note, bool Tail)> written = new List<(TextSpan, ChartNote, bool)>();
        bool sorted = true;

        internal void Add(object item, TextSpan span) => spans[item] = span;

        internal void AddNote(ChartNote note, TextSpan span)
        {
            spans[note] = span;
            Written(span, note, false);
        }

        // A roll's end. A roll shared by the routes is ended on each; its first end is kept as its
        // tail (as its EndTime is), every one is found by NoteAt.
        internal void AddTail(ChartNote note, TextSpan span)
        {
            if (!tails.ContainsKey(note)) tails[note] = span;
            Written(span, note, true);
        }

        void Written(TextSpan span, ChartNote note, bool tail)
        {
            if (written.Count > 0 && written[written.Count - 1].Span.Start > span.Start) sorted = false;
            written.Add((span, note, tail));
        }

        public bool TryGetSpan(object item, out TextSpan span) => spans.TryGetValue(item, out span);

        public bool TryGetTailSpan(ChartNote note, out TextSpan span) => tails.TryGetValue(note, out span);

        // The note written at an offset, or null; `tail` says it is a roll's end there. Where a
        // note ends a roll, the note itself is given.
        public ChartNote NoteAt(int offset, out bool tail)
        {
            if (!sorted)
            {
                written.Sort((a, b) => a.Span.Start.CompareTo(b.Span.Start));
                sorted = true;
            }
            int lo = 0, hi = written.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (written[mid].Span.Start <= offset) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            if (found < 0 || !written[found].Span.Contains(offset)) { tail = false; return null; }
            // Of the entries written at the same place (a note that ends a roll), the head.
            int start = written[found].Span.Start;
            for (int k = found; k >= 0 && written[k].Span.Start == start; k--)
                if (!written[k].Tail) { found = k; break; }
            tail = written[found].Tail;
            return written[found].Note;
        }

        public ChartNote NoteAt(int offset) => NoteAt(offset, out _);
    }
}
