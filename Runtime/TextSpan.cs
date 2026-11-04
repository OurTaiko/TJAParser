using System;
using System.Collections.Generic;
using System.Globalization;

namespace OurTaiko
{
    // A stretch of a TJA text: Start is a UTF-16 index into the text as given (a BOM included),
    // End is just past its last character.
    public readonly struct TextSpan : IEquatable<TextSpan>
    {
        public readonly int Start, Length;

        public TextSpan(int start, int length)
        {
            Start = start; Length = length;
        }

        public int End => Start + Length;

        public static TextSpan FromBounds(int start, int end) => new TextSpan(start, end - start);

        public bool Contains(int offset) => offset >= Start && offset < End;

        public bool Equals(TextSpan other) => Start == other.Start && Length == other.Length;
        public override bool Equals(object obj) => obj is TextSpan other && Equals(other);
        public override int GetHashCode() => Start * 397 ^ Length;
        public static bool operator ==(TextSpan a, TextSpan b) => a.Equals(b);
        public static bool operator !=(TextSpan a, TextSpan b) => !a.Equals(b);
        public override string ToString() => "[" + Start.ToString(CultureInfo.InvariantCulture) + ".." + End.ToString(CultureInfo.InvariantCulture) + ")";
    }

    // Lines and columns of a TJA text, with the breaks TjaLines reads (CRLF, a lone CR, LF). Lines
    // count from 1, as TjaLine.Line and the warnings do; columns from 0 in UTF-16 code units, as
    // LSP's `character` (a BOM at the start counts as one).
    public sealed class TjaSourceText
    {
        readonly int[] starts;
        readonly int length;

        public TjaSourceText(string text)
        {
            text = text ?? "";
            length = text.Length;
            var list = new List<int> { 0 };
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                if (c == '\r' || c == '\n') list.Add(i + 1);
            }
            starts = list.ToArray();
        }

        public int LineCount => starts.Length;

        // Where a line (from 1) starts.
        public int LineStart(int line) => starts[Math.Max(1, Math.Min(line, starts.Length)) - 1];

        // The line (from 1) an offset is on; past the end, the last line.
        public int Line(int offset)
        {
            int i = Array.BinarySearch(starts, Math.Max(0, Math.Min(offset, length)));
            return (i >= 0 ? i : ~i - 1) + 1;
        }

        public (int Line, int Column) Position(int offset)
        {
            offset = Math.Max(0, Math.Min(offset, length));
            int line = Line(offset);
            return (line, offset - starts[line - 1]);
        }

        public int Offset(int line, int column) => Math.Min(LineStart(line) + Math.Max(0, column), length);
    }
}
