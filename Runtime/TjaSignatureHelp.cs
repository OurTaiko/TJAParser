using System;
using System.Collections.Generic;
using System.Text;

namespace OurTaiko
{
    // A command's parameters while its arguments are written: the signature, where each parameter
    // is in it, and the one the position is on.
    public sealed class TjaSignature
    {
        public TjaKeyword Keyword { get; internal set; }
        // As TjaHover.Signature writes it.
        public string Label { get; internal set; }
        // Each parameter's stretch of Label (start, length).
        public IReadOnlyList<(int Start, int Length)> Parameters { get; internal set; }
        public int Active { get; internal set; }
    }

    public static class TjaSignatureHelp
    {
        // Null where no command with parameters is being written: on a command line, after its
        // name and a space or tab, outside comments.
        public static TjaSignature At(TjaDocument document, int offset)
        {
            string text = document.Text;
            int lineStart = document.Lines.LineStart(document.Lines.Line(offset));
            int first = lineStart;
            while (first < offset && char.IsWhiteSpace(text[first])) first++;
            string before = text.Substring(first, offset - first);
            if (!before.StartsWith("#", StringComparison.Ordinal) || before.Contains("//") || before.Contains("/*")) return null;
            int space = before.IndexOfAny(new[] { ' ', '\t' });
            if (space < 0) return null;
            var keyword = TjaSchema.Find(before.Substring(0, space));
            if (keyword == null || keyword.Parameters.Count == 0) return null;
            string args = before.Substring(space + 1);
            int active;
            if (keyword.Parameters.Count == 1) active = 0;
            else if (keyword.Name == "#BRANCHSTART") active = Count(args, ',');
            else active = Words(args);
            return Signature(keyword, Math.Min(active, keyword.Parameters.Count - 1));
        }

        static int Count(string s, char c)
        {
            int n = 0;
            foreach (char x in s) if (x == c) n++;
            return n;
        }

        // The parameters already written: words followed by a space or tab.
        static int Words(string args)
        {
            int n = 0;
            bool inWord = false;
            foreach (char c in args)
            {
                bool blank = c == ' ' || c == '\t';
                if (blank && inWord) n++;
                inWord = !blank;
            }
            return n;
        }

        static TjaSignature Signature(TjaKeyword keyword, int active)
        {
            string label = TjaHover.Signature(keyword);
            // Each parameter is written as <name> or [name], in order.
            var parameters = new List<(int, int)>();
            int at = keyword.Name.Length;
            for (int i = 0; i < keyword.Parameters.Count; i++)
            {
                int start = label.IndexOfAny(new[] { '<', '[' }, at);
                int end = label.IndexOfAny(new[] { '>', ']' }, start) + 1;
                parameters.Add((start, end - start));
                at = end;
            }
            return new TjaSignature { Keyword = keyword, Label = label, Parameters = parameters, Active = active };
        }
    }
}
