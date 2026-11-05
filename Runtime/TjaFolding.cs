using System;
using System.Collections.Generic;

namespace OurTaiko
{
    public enum TjaFoldingKind { Block, Branch, Route, Comment }

    // A stretch an editor may fold: from StartLine to EndLine (lines from 1, both shown when folded
    // at the first).
    public readonly struct TjaFoldingRange
    {
        public readonly int StartLine, EndLine;
        public readonly TjaFoldingKind Kind;

        public TjaFoldingRange(int startLine, int endLine, TjaFoldingKind kind)
        {
            StartLine = startLine; EndLine = endLine; Kind = kind;
        }

        public override string ToString() => $"{Kind} {StartLine}-{EndLine}";
    }

    public static class TjaFolding
    {
        // Blocks (#START to their end), branches (#BRANCHSTART to #BRANCHEND, or where the branch
        // ends without it), routes (#N, #E, #M to before the next marker or the branch's end), and
        // /* */ comments over several lines. Only what spans two lines or more.
        public static List<TjaFoldingRange> Ranges(TjaDocument document)
        {
            var ranges = new List<TjaFoldingRange>();
            void Add(int start, int end, TjaFoldingKind kind)
            {
                if (end > start) ranges.Add(new TjaFoldingRange(start, end, kind));
            }
            int block = 0, branch = 0, route = 0;
            foreach (var line in TjaLines.Read(document.Text))
            {
                if (line.Kind == TjaLineKind.Start) { block = line.Line; continue; }
                if (line.Kind == TjaLineKind.End)
                {
                    // An implicit end is at the line that ended the block: the block is before it.
                    int end = line.Implicit ? Math.Max(block, line.Line - 1) : line.Line;
                    if (route > 0) Add(route, line.Implicit ? end : end - 1, TjaFoldingKind.Route);
                    if (branch > 0) Add(branch, line.Implicit ? end : end - 1, TjaFoldingKind.Branch);
                    Add(block, end, TjaFoldingKind.Block);
                    block = branch = route = 0;
                    continue;
                }
                if (line.Kind != TjaLineKind.Body) continue;
                if (line.Text[0] != '#') continue;
                int space = line.Text.IndexOfAny(new[] { ' ', '\t' });
                string key = (space < 0 ? line.Text : line.Text.Substring(0, space)).ToUpperInvariant();
                switch (key)
                {
                    case "#BRANCHSTART":
                        if (route > 0) Add(route, line.Line - 1, TjaFoldingKind.Route);
                        if (branch > 0) Add(branch, line.Line - 1, TjaFoldingKind.Branch);
                        branch = line.Line; route = 0;
                        break;
                    case "#N": case "#E": case "#M":
                        if (route > 0) Add(route, line.Line - 1, TjaFoldingKind.Route);
                        if (branch > 0) route = line.Line;
                        break;
                    case "#BRANCHEND":
                        if (route > 0) Add(route, line.Line - 1, TjaFoldingKind.Route);
                        if (branch > 0) Add(branch, line.Line, TjaFoldingKind.Branch);
                        branch = route = 0;
                        break;
                }
            }
            // Comments: a /* not closed on its line, to the line where its */ is (or the last line).
            var lines = document.Lines;
            string text = document.Text;
            int opened = 0;
            foreach (var token in document.Syntax)
            {
                if (token.Kind != TjaSyntaxKind.Comment) continue;
                string body = text.Substring(token.Span.Start, token.Span.Length);
                if (opened == 0)
                {
                    bool closedHere = body.Length >= 4 && body.EndsWith("*/", StringComparison.Ordinal);
                    if (body.StartsWith("/*", StringComparison.Ordinal) && !closedHere) opened = lines.Line(token.Span.Start);
                }
                else if (body.Contains("*/"))
                {
                    Add(opened, lines.Line(token.Span.Start), TjaFoldingKind.Comment);
                    opened = 0;
                }
            }
            if (opened > 0) Add(opened, lines.LineCount, TjaFoldingKind.Comment);
            ranges.Sort((a, b) => a.StartLine != b.StartLine ? a.StartLine.CompareTo(b.StartLine) : b.EndLine.CompareTo(a.EndLine));
            return ranges;
        }
    }
}
