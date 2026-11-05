using System;
using System.Collections.Generic;
using System.Threading;

namespace OurTaiko
{
    // A replacement of a stretch of the text.
    public readonly struct TjaTextEdit
    {
        public readonly TextSpan Span;
        public readonly string NewText;

        public TjaTextEdit(TextSpan span, string newText)
        {
            Span = span; NewText = newText;
        }

        // The text with edits that do not overlap made, the last first.
        public static string Apply(string text, IEnumerable<TjaTextEdit> edits)
        {
            var list = new List<TjaTextEdit>(edits);
            list.Sort((a, b) => b.Span.Start.CompareTo(a.Span.Start));
            foreach (var e in list) text = text.Substring(0, e.Span.Start) + e.NewText + text.Substring(e.Span.End);
            return text;
        }
    }

    public sealed class TjaCodeAction
    {
        public string Title { get; internal set; }
        public IReadOnlyList<TjaTextEdit> Edits { get; internal set; }
        // What it fixes, if a diagnostic.
        public TjaDiagnostic Fixes { get; internal set; }
    }

    // What an editor can offer to mend a diagnostic, and to rewrite a block's balloon counts.
    public static class TjaQuickFix
    {
        public static List<TjaCodeAction> For(TjaDocument document, TjaDiagnostic d)
        {
            var actions = new List<TjaCodeAction>();
            void Add(string title, params TjaTextEdit[] edits) => actions.Add(new TjaCodeAction { Title = title, Edits = edits, Fixes = d });
            string text = document.Text;
            if (d.Kind == TjaDiagnosticKind.LastMeasureWithoutComma)
                Add("End the measure with a comma", new TjaTextEdit(new TextSpan(d.Span.End, 0), ","));
            else if (d.Kind == TjaDiagnosticKind.MissingCommaBeforeBranch)
                Add("End the measure before the branch", new TjaTextEdit(new TextSpan(document.Lines.LineStart(d.Line), 0), ",\n"));
            else if (d.Kind == TjaDiagnosticKind.EndMissing)
            {
                // Where the block ended: the start of the line that ended it, or the end of the file.
                bool atEnd = d.Span.Start >= text.Length;
                string insert = atEnd ? (text.Length > 0 && text[text.Length - 1] != '\n' && text[text.Length - 1] != '\r' ? "\n#END\n" : "#END\n") : "#END\n";
                Add("Write the missing #END", new TjaTextEdit(new TextSpan(d.Span.Start, 0), insert));
            }
            else if (d.Kind == TjaDiagnosticKind.SimilarCommand)
            {
                string name = (string)d.Args[0];
                int nameEnd = d.Span.Start;
                while (nameEnd < d.Span.End && text[nameEnd] != ' ' && text[nameEnd] != '\t') nameEnd++;
                Add("Change to " + name, new TjaTextEdit(TextSpan.FromBounds(d.Span.Start, nameEnd), name));
            }
            return actions;
        }

        // Every balloon of a block without its count written on it gets it, `7` becoming `7[n]`: the
        // chart plays the same (an inline count still takes its place in the lists). Null when every
        // balloon has one already, or the block does not parse.
        public static TjaCodeAction InlineBalloons(TjaDocument document, string course, string side, CancellationToken cancel = default)
        {
            var analysis = document.Analyze(course, side, cancel);
            if (analysis.Chart == null || analysis.HasErrors) return null;
            var edits = new List<TjaTextEdit>();
            foreach (var note in analysis.Chart.Notes)
                if (note.IsBalloon && analysis.SourceMap.TryGetSpan(note, out var span) && span.Length == 1)
                    edits.Add(new TjaTextEdit(new TextSpan(span.End, 0), "[" + note.BalloonHits + "]"));
            return edits.Count == 0 ? null : new TjaCodeAction { Title = "Write the balloon counts on the balloons", Edits = edits };
        }
    }
}
