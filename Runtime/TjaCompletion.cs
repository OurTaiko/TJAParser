using System;
using System.Collections.Generic;
using System.Threading;

namespace OurTaiko
{
    public enum TjaCompletionKind
    {
        Header,
        Command,
        // A header's value or a command's argument (COURSE's Oni, #START's P1, #BRANCHSTART's p).
        Value,
        Note,
    }

    // A completion: Insert replaces the text at Replace (what has been typed of it).
    public sealed class TjaCompletionItem
    {
        public string Label { get; internal set; }
        public TjaCompletionKind Kind { get; internal set; }
        public string Detail { get; internal set; }
        public string Insert { get; internal set; }
        public TextSpan Replace { get; internal set; }

        public override string ToString() => Label;
    }

    // What can be written at a position: by what is written before it on its line, and whether it
    // is in a block (and in a branch there). Nothing in a comment.
    public static class TjaCompletion
    {
        // The note characters, in the order offered (TjaText.NoteCharacter says what they are).
        static readonly string[] Notes = { "1", "2", "3", "4", "5", "6", "7", "9", "8", "0", "G", "C", "F", "," };

        // Details in a language ("zh…", "ja…"; anything else, or null, is English).
        public static List<TjaCompletionItem> At(TjaDocument document, int offset, CancellationToken cancel = default, string language = null)
        {
            var items = new List<TjaCompletionItem>();
            string text = document.Text;
            offset = Math.Max(0, Math.Min(offset, text.Length));
            if (InComment(document, offset)) return items;
            int lineStart = document.Lines.LineStart(document.Lines.Line(offset));
            // What is written before the position on its line, from its first character.
            int first = lineStart;
            while (first < offset && char.IsWhiteSpace(text[first])) first++;
            string before = text.Substring(first, offset - first);
            var block = document.BlockAt(offset);
            // The #START line itself (its player) is written as outside the block.
            bool onStartLine = block != null && document.Lines.Line(offset) == block.Line;
            if (block == null || onStartLine) Outside(before, first, offset, items, language);
            else Inside(document, block, before, first, offset, items, cancel, language);
            return items;
        }

        static bool InComment(TjaDocument document, int offset)
        {
            // The token the position is in or right after.
            foreach (var token in document.Syntax)
                if (token.Span.Start < offset && offset <= token.Span.End) return token.Kind == TjaSyntaxKind.Comment;
            return false;
        }

        static void Add(List<TjaCompletionItem> items, string label, TjaCompletionKind kind, string detail, string insert, int from, int to)
            => items.Add(new TjaCompletionItem { Label = label, Kind = kind, Detail = detail, Insert = insert, Replace = TextSpan.FromBounds(from, to) });

        static void Outside(string before, int first, int offset, List<TjaCompletionItem> items, string language)
        {
            int colon = before.IndexOf(':');
            if (before.StartsWith("#", StringComparison.Ordinal))
            {
                int space = before.IndexOfAny(new[] { ' ', '\t' });
                if (space < 0)
                {
                    foreach (var k in TjaSchema.Commands)
                        if ((k.Scope & TjaScope.Outside) != 0 && Starts(k.Name, before))
                            Add(items, k.Name, TjaCompletionKind.Command, k.SummaryIn(language), k.Name, first, offset);
                }
                else if (before.Substring(0, space).Equals("#START", StringComparison.OrdinalIgnoreCase))
                    Values(TjaSchema.Find("#START").Parameters[0], before.Substring(space), first + space, offset, items);
            }
            else if (colon < 0)
            {
                foreach (var k in TjaSchema.Headers)
                    if (Starts(k.Name, before)) Add(items, k.Name, TjaCompletionKind.Header, k.SummaryIn(language), k.Name + ":", first, offset);
            }
            else
            {
                var header = TjaSchema.Find(before.Substring(0, colon).Trim());
                if (header == null || header.Parameters.Count == 0) return;
                var parameter = header.Parameters[0];
                if (header.Name == "LEVEL")
                {
                    for (int stars = 1; stars <= 10; stars++) AddValue(items, stars.ToString(), TjaText.Stars(language), before.Substring(colon + 1), first + colon + 1, offset);
                    return;
                }
                Values(parameter, before.Substring(colon + 1), first + colon + 1, offset, items);
            }
        }

        // A parameter's values, after `typed` (which starts at `from`, spaces first).
        static void Values(TjaParameter parameter, string typed, int from, int offset, List<TjaCompletionItem> items)
        {
            foreach (string value in parameter.Values) AddValue(items, value, parameter.Name, typed, from, offset);
        }

        static void AddValue(List<TjaCompletionItem> items, string value, string detail, string typed, int from, int offset)
        {
            int lead = 0;
            while (lead < typed.Length && char.IsWhiteSpace(typed[lead])) lead++;
            if (Starts(value, typed.Substring(lead))) Add(items, value, TjaCompletionKind.Value, detail, value, from + lead, offset);
        }

        static void Inside(TjaDocument document, TjaDocumentBlock block, string before, int first, int offset, List<TjaCompletionItem> items, CancellationToken cancel, string language)
        {
            if (before.StartsWith("#", StringComparison.Ordinal))
            {
                int space = before.IndexOfAny(new[] { ' ', '\t' });
                if (space >= 0)
                {
                    // #BRANCHSTART's condition, before its first comma.
                    if (before.Substring(0, space).Equals("#BRANCHSTART", StringComparison.OrdinalIgnoreCase) && before.IndexOf(',') < 0)
                        foreach (string value in TjaSchema.Find("#BRANCHSTART").Parameters[0].Values)
                            AddValue(items, value, TjaText.Condition(value, language), before.Substring(space), first + space, offset);
                    return;
                }
                Commands(document, block, before, first, offset, items, cancel, language);
                return;
            }
            // A note line (or a line not begun): the notes, and at its start the commands too.
            foreach (char c in before)
                if (!char.IsWhiteSpace(c) && c != ',' && TjaCompletion.NoteIndex(c) < 0 && c != '[' && c != ']' && !char.IsDigit(c)) return;
            foreach (string note in Notes) Add(items, note, TjaCompletionKind.Note, TjaText.NoteCharacter(note, language), note, offset, offset);
            if (before.Length == 0) Commands(document, block, "", first, offset, items, cancel, language);
        }

        static int NoteIndex(char c) => "0123456789CFG".IndexOf(c);

        // The block's commands; the route markers and #BRANCHEND only in a branch.
        static void Commands(TjaDocument document, TjaDocumentBlock block, string typed, int first, int offset, List<TjaCompletionItem> items, CancellationToken cancel, string language)
        {
            var state = document.Analyze(block.Course, block.Side, cancel).StateAt(offset, cancel);
            bool inBranch = state != null && state.BranchId >= 0;
            foreach (var k in TjaSchema.Commands)
            {
                if ((k.Scope & TjaScope.Body) == 0 || !Starts(k.Name, typed)) continue;
                bool branchOnly = k.Name == "#N" || k.Name == "#E" || k.Name == "#M" || k.Name == "#BRANCHEND";
                if (branchOnly && !inBranch) continue;
                Add(items, k.Name, TjaCompletionKind.Command, k.SummaryIn(language), k.Name, first, offset);
            }
        }

        static bool Starts(string name, string typed) => name.StartsWith(typed, StringComparison.OrdinalIgnoreCase);
    }
}
