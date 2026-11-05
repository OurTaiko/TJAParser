using OurTaiko;

namespace OurTaiko.LanguageServer;

// The syntax tokens as LSP semantic tokens: whitespace, line breaks and the BOM are left out,
// every other character is in one token. Notes have types of their own (don, ka…), the rest the
// standard ones.
public static class SemanticTokens
{
    public static readonly string[] Types =
    {
        "comment", "property", "operator", "string", "keyword", "parameter", "number",
        "don", "ka", "bigDon", "bigKa", "roll", "balloon", "kadon", "mine", "adlib", "blank", "rollEnd", "unknownNote",
    };

    // The type of a syntax token, or -1 for one left out.
    public static int Type(TjaSyntaxToken token) => token.Kind switch
    {
        TjaSyntaxKind.Comment => 0,
        TjaSyntaxKind.HeaderKey => 1,
        TjaSyntaxKind.Colon or TjaSyntaxKind.Comma => 2,
        TjaSyntaxKind.HeaderValue or TjaSyntaxKind.Text => 3,
        TjaSyntaxKind.Command => 4,
        TjaSyntaxKind.Argument => 5,
        TjaSyntaxKind.InlineCount => 6,
        TjaSyntaxKind.Blank => 16,
        TjaSyntaxKind.RollEnd => 17,
        TjaSyntaxKind.Unknown => 18,
        TjaSyntaxKind.Note => token.Note switch
        {
            NoteKind.Don => 7, NoteKind.Ka => 8, NoteKind.BigDon => 9, NoteKind.BigKa => 10,
            NoteKind.Roll or NoteKind.BigRoll => 11, NoteKind.Balloon or NoteKind.Kusudama => 12,
            NoteKind.Kadon => 13, NoteKind.Mine => 14, NoteKind.AdLib => 15, _ => 18,
        },
        _ => -1,
    };

    // LSP's encoding: per token, its line and start (each from the token before), length, type, modifiers.
    public static List<int> Encode(TjaDocument document)
    {
        var data = new List<int>();
        int previousLine = 0, previousStart = 0;
        foreach (var token in document.Syntax)
        {
            int type = Type(token);
            if (type < 0) continue;
            var (line, column) = document.Lines.Position(token.Span.Start);
            line--;
            data.Add(line - previousLine);
            data.Add(line == previousLine ? column - previousStart : column);
            data.Add(token.Span.Length);
            data.Add(type);
            data.Add(0);
            previousLine = line; previousStart = column;
        }
        return data;
    }
}
