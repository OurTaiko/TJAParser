using System.Text.Json.Nodes;

namespace OurTaiko.LanguageServer;

// The language features over LSP: each asks the library (OurTaiko) and turns the answer into the
// protocol's terms (ranges in lines and UTF-16 characters from 0).
public sealed partial class Server
{
    static partial void FeatureCapabilities(JsonObject capabilities)
    {
        capabilities["completionProvider"] = new JsonObject { ["triggerCharacters"] = new JsonArray("#", ":", " ") };
        capabilities["hoverProvider"] = true;
        capabilities["foldingRangeProvider"] = true;
        var types = new JsonArray();
        foreach (string type in SemanticTokens.Types) types.Add(type);
        capabilities["semanticTokensProvider"] = new JsonObject
        {
            ["legend"] = new JsonObject { ["tokenTypes"] = types, ["tokenModifiers"] = new JsonArray() }, ["full"] = true,
        };
        capabilities["signatureHelpProvider"] = new JsonObject
        {
            ["triggerCharacters"] = new JsonArray(" ", ","), ["retriggerCharacters"] = new JsonArray(" ", ","),
        };
    }

    partial void Features(string method, JsonObject p, CancellationToken cancel, ref JsonNode result, ref bool handled)
    {
        switch (method)
        {
            case "textDocument/completion":
                handled = true;
                result = At(p, out var document, out int offset) ? Completion(document, offset, cancel) : null;
                break;
            case "textDocument/signatureHelp":
                handled = true;
                if (At(p, out document, out offset) && TjaSignatureHelp.At(document, offset) is TjaSignature signature)
                {
                    var parameters = new JsonArray();
                    foreach (var (start, length) in signature.Parameters) parameters.Add(new JsonObject { ["label"] = new JsonArray(start, start + length) });
                    result = new JsonObject
                    {
                        ["signatures"] = new JsonArray(new JsonObject
                        {
                            ["label"] = signature.Label, ["documentation"] = signature.Keyword.Summary, ["parameters"] = parameters,
                        }),
                        ["activeSignature"] = 0, ["activeParameter"] = signature.Active,
                    };
                }
                break;
            case "textDocument/semanticTokens/full":
                handled = true;
                if (documents.TryGetValue((string)p["textDocument"]["uri"], out document))
                {
                    var data = new JsonArray();
                    foreach (int n in SemanticTokens.Encode(document)) data.Add(n);
                    result = new JsonObject { ["data"] = data };
                }
                break;
            case "textDocument/foldingRange":
                handled = true;
                if (documents.TryGetValue((string)p["textDocument"]["uri"], out document))
                {
                    var ranges = new JsonArray();
                    foreach (var range in TjaFolding.Ranges(document))
                        ranges.Add(new JsonObject
                        {
                            ["startLine"] = range.StartLine - 1, ["endLine"] = range.EndLine - 1,
                            ["kind"] = range.Kind == TjaFoldingKind.Comment ? "comment" : "region",
                        });
                    result = ranges;
                }
                break;
            case "textDocument/hover":
                handled = true;
                if (At(p, out document, out offset) && TjaHover.At(document, offset, cancel) is TjaHoverInfo hover)
                    result = new JsonObject
                    {
                        ["contents"] = new JsonObject { ["kind"] = "markdown", ["value"] = hover.Markdown },
                        ["range"] = Range(document, hover.Span),
                    };
                break;
        }
    }

    // The document and offset a request is about; false for a document not open.
    bool At(JsonObject p, out TjaDocument document, out int offset)
    {
        offset = 0;
        if (!documents.TryGetValue((string)p["textDocument"]["uri"], out document)) return false;
        offset = Offset(document, p["position"]);
        return true;
    }

    static JsonNode Completion(TjaDocument document, int offset, CancellationToken cancel)
    {
        var items = new JsonArray();
        int order = 0;
        foreach (var item in TjaCompletion.At(document, offset, cancel))
            items.Add(new JsonObject
            {
                ["label"] = item.Label,
                ["kind"] = item.Kind switch { TjaCompletionKind.Header => 10, TjaCompletionKind.Command => 14, TjaCompletionKind.Value => 20, _ => 21 },
                ["detail"] = item.Detail,
                ["sortText"] = (order++).ToString("D4"),
                ["textEdit"] = new JsonObject { ["range"] = Range(document, item.Replace), ["newText"] = item.Insert },
            });
        return new JsonObject { ["isIncomplete"] = false, ["items"] = items };
    }
}
