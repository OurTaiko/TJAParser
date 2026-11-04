using System;
using System.Collections.Generic;

namespace OurTaiko
{
    public enum TjaSyntaxKind
    {
        Whitespace,
        // \r\n, \r or \n.
        NewLine,
        // A byte order mark at the start.
        Bom,
        // // to the end of the line, or /* … */ (one token per line it spans).
        Comment,
        // A header's KEY, its colon and its value (KEY:VALUE outside a block).
        HeaderKey,
        Colon,
        HeaderValue,
        // A command's #NAME (also #START and #END), and what follows it.
        Command,
        Argument,
        // In a note line: a note (Note says which), 0, 8, an inline balloon count [n], a comma.
        Note,
        Blank,
        RollEnd,
        InlineCount,
        Comma,
        // A character of a note line that is no note (E, lower case, other symbols).
        Unknown,
        // Anything else outside a block (a line that is neither header nor command).
        Text,
    }

    public readonly struct TjaSyntaxToken
    {
        public readonly TjaSyntaxKind Kind;
        public readonly TextSpan Span;
        // For a Note, its kind.
        public readonly NoteKind? Note;

        public TjaSyntaxToken(TjaSyntaxKind kind, TextSpan span, NoteKind? note = null)
        {
            Kind = kind; Span = span; Note = note;
        }

        public override string ToString() => Kind + " " + Span;
    }

    // The whole text as tokens for an editor (highlighting, folding, formatting): every character
    // is in one token, so the tokens put together are the text. Lines, comments and blocks are
    // read by the rules TjaLines reads them by, and each line's tokens follow its kind there.
    public static class TjaSyntax
    {
        const string Base36 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        public static List<TjaSyntaxToken> Tokenize(string text)
        {
            text = text ?? "";
            int n = text.Length;
            var kinds = new TjaSyntaxKind[n];
            MarkLayout(text, kinds);

            // What each kept character of a line is, by the line's kind.
            var positions = new List<LineSource>();
            int index = -1;
            foreach (var line in TjaLines.Read(text, positions))
            {
                index++;
                if (line.Implicit) continue;
                var source = positions[index];
                var roles = Roles(line);
                for (int i = 0; i < roles.Length; i++)
                {
                    int at = source.SourceOffset(i);
                    // The space a /* */ leaves in a line stands for the comment, not for the text.
                    if (at < n && kinds[at] == TjaSyntaxKind.Whitespace) kinds[at] = roles[i];
                }
            }

            var tokens = new List<TjaSyntaxToken>();
            for (int start = 0; start < n;)
            {
                var kind = kinds[start];
                int end = start + 1;
                if (Merges(kind))
                    while (end < n && kinds[end] == kind && !(kind == TjaSyntaxKind.NewLine) && !(kind == TjaSyntaxKind.Comment && IsBreak(text[end]))) end++;
                if (kind == TjaSyntaxKind.NewLine && text[start] == '\r' && end < n && text[end] == '\n') end++;
                NoteKind? note = null;
                if (kind == TjaSyntaxKind.Note) note = (NoteKind)Base36.IndexOf(text[start]);
                tokens.Add(new TjaSyntaxToken(kind, TextSpan.FromBounds(start, end), note));
                start = end;
            }
            return tokens;
        }

        // Notes, blanks, roll ends and commas are a character each; the rest run on.
        static bool Merges(TjaSyntaxKind kind) => kind != TjaSyntaxKind.Note && kind != TjaSyntaxKind.Blank
            && kind != TjaSyntaxKind.RollEnd && kind != TjaSyntaxKind.Comma && kind != TjaSyntaxKind.Unknown;

        static bool IsBreak(char c) => c == '\r' || c == '\n';

        // Line breaks, the BOM and comments, as TjaLines' LineReader finds them; the rest is
        // whitespace until a line's kind says what it is.
        static void MarkLayout(string text, TjaSyntaxKind[] kinds)
        {
            int n = text.Length, i = 0;
            while (i < n && text[i] == '﻿') kinds[i++] = TjaSyntaxKind.Bom;
            bool inComment = false;
            while (i <= n)
            {
                int end = text.IndexOfAny(new[] { '\r', '\n' }, Math.Min(i, n));
                if (end < 0) end = n;
                // The line [i, end): comments in it.
                for (int k = i; k < end;)
                {
                    if (inComment)
                    {
                        int close = text.IndexOf("*/", k, end - k, StringComparison.Ordinal);
                        int stop = close < 0 ? end : close + 2;
                        for (int c = k; c < stop; c++) kinds[c] = TjaSyntaxKind.Comment;
                        if (close < 0) break;
                        inComment = false;
                        k = stop;
                        continue;
                    }
                    int lineComment = text.IndexOf("//", k, end - k, StringComparison.Ordinal);
                    int block = text.IndexOf("/*", k, end - k, StringComparison.Ordinal);
                    if (block < 0 || (lineComment >= 0 && lineComment < block))
                    {
                        if (lineComment >= 0) for (int c = lineComment; c < end; c++) kinds[c] = TjaSyntaxKind.Comment;
                        break;
                    }
                    for (int c = block; c < block + 2; c++) kinds[c] = TjaSyntaxKind.Comment;
                    inComment = true;
                    k = block + 2;
                }
                if (end >= n) break;
                kinds[end] = TjaSyntaxKind.NewLine;
                if (text[end] == '\r' && end + 1 < n && text[end + 1] == '\n') { kinds[end + 1] = TjaSyntaxKind.NewLine; end++; }
                i = end + 1;
            }
        }

        // The kind of each character of a line's text (TjaLine.Text, comments taken out).
        static TjaSyntaxKind[] Roles(TjaLine line)
        {
            string text = line.Text;
            var roles = new TjaSyntaxKind[text.Length];
            switch (line.Kind)
            {
                case TjaLineKind.Header:
                    int colon = text.IndexOf(':');
                    Fill(roles, text, 0, colon, TjaSyntaxKind.HeaderKey);
                    roles[colon] = TjaSyntaxKind.Colon;
                    Fill(roles, text, colon + 1, text.Length, TjaSyntaxKind.HeaderValue);
                    break;
                case TjaLineKind.Start:
                    Fill(roles, text, 0, 6, TjaSyntaxKind.Command);
                    Fill(roles, text, 6, text.Length, TjaSyntaxKind.Argument);
                    break;
                case TjaLineKind.Body when text[0] != '#':
                    Notes(text, roles);
                    break;
                case TjaLineKind.Other when text[0] != '#':
                    Fill(roles, text, 0, text.Length, TjaSyntaxKind.Text);
                    break;
                default:
                    // A command: its name up to the first space or tab, then its argument.
                    int space = text.IndexOfAny(new[] { ' ', '\t' });
                    if (space < 0) space = text.Length;
                    Fill(roles, text, 0, space, TjaSyntaxKind.Command);
                    Fill(roles, text, space, text.Length, TjaSyntaxKind.Argument);
                    break;
            }
            return roles;
        }

        // [from, to) as `kind`, its leading and trailing whitespace as whitespace.
        static void Fill(TjaSyntaxKind[] roles, string text, int from, int to, TjaSyntaxKind kind)
        {
            int a = from, b = to;
            while (a < b && char.IsWhiteSpace(text[a])) a++;
            while (b > a && char.IsWhiteSpace(text[b - 1])) b--;
            for (int i = from; i < to; i++) roles[i] = i >= a && i < b ? kind : TjaSyntaxKind.Whitespace;
        }

        static void Notes(string text, TjaSyntaxKind[] roles)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) { roles[i] = TjaSyntaxKind.Whitespace; continue; }
                if (c == '[')
                {
                    // To its ], or to the end of the line when there is none.
                    int close = text.IndexOf(']', i);
                    int end = close < 0 ? text.Length : close + 1;
                    for (int k = i; k < end; k++) roles[k] = TjaSyntaxKind.InlineCount;
                    i = end - 1;
                    continue;
                }
                int type = Base36.IndexOf(c);
                roles[i] = c == ',' ? TjaSyntaxKind.Comma
                    : type == 0 ? TjaSyntaxKind.Blank
                    : type == 8 ? TjaSyntaxKind.RollEnd
                    : type > 0 && Enum.IsDefined(typeof(NoteKind), type) ? TjaSyntaxKind.Note
                    : TjaSyntaxKind.Unknown;
            }
        }
    }
}
