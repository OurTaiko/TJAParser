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
        capabilities["inlayHintProvider"] = true;
        capabilities["codeActionProvider"] = true;
        capabilities["documentFormattingProvider"] = true;
        capabilities["codeLensProvider"] = new JsonObject { ["resolveProvider"] = false };
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
            // A preview following the cursor: the block, time, measure and route at a position.
            case "tja/timeAt":
                handled = true;
                if (At(p, out document, out offset) && document.BlockAt(offset) is TjaDocumentBlock block
                    && document.Analyze(block.Course, block.Side, cancel).StateAt(offset, cancel) is TjaPositionState state)
                    result = new JsonObject
                    {
                        ["course"] = block.Course, ["side"] = block.Side, ["time"] = state.Time, ["measure"] = state.Measure,
                        ["branch"] = state.BranchId, ["route"] = state.Route?.ToString(),
                    };
                break;
            // A note clicked in a preview: where the note nearest a time on a route is written.
            case "tja/noteAt":
                handled = true;
                if (documents.TryGetValue((string)p["textDocument"]["uri"], out document))
                {
                    var analysis = document.Analyze((string)p["course"] ?? "Oni", (string)p["side"] ?? "single", cancel);
                    var route = Enum.TryParse<BranchRoute>((string)p["route"] ?? "Normal", out var r) ? r : BranchRoute.Normal;
                    if (analysis.NoteNear((double)p["time"], route) is ChartNote note && analysis.SourceMap.TryGetSpan(note, out var span))
                        result = new JsonObject
                        {
                            ["uri"] = (string)p["textDocument"]["uri"], ["range"] = Range(document, span),
                            ["time"] = note.Time, ["kind"] = note.Kind.ToString(),
                        };
                }
                break;
            case "textDocument/inlayHint":
                handled = true;
                if (documents.TryGetValue((string)p["textDocument"]["uri"], out document))
                {
                    var range = TextSpan.FromBounds(Offset(document, p["range"]["start"]), Offset(document, p["range"]["end"]));
                    var hints = new JsonArray();
                    foreach (var hint in TjaSummary.Hints(document, range, cancel))
                        hints.Add(new JsonObject { ["position"] = Position(document, hint.Offset), ["label"] = hint.Label, ["paddingRight"] = true });
                    result = hints;
                }
                break;
            case "textDocument/codeLens":
                handled = true;
                if (documents.TryGetValue((string)p["textDocument"]["uri"], out document))
                {
                    var lenses = new JsonArray();
                    foreach (var summary in TjaSummary.Blocks(document, cancel))
                        lenses.Add(new JsonObject
                        {
                            ["range"] = Range(document, summary.Block.StartSpan),
                            ["command"] = new JsonObject { ["title"] = summary.Title, ["command"] = "" },
                        });
                    result = lenses;
                }
                break;
            case "textDocument/codeAction":
                handled = true;
                if (documents.TryGetValue((string)p["textDocument"]["uri"], out document))
                {
                    string uri = (string)p["textDocument"]["uri"];
                    var range = TextSpan.FromBounds(Offset(document, p["range"]["start"]), Offset(document, p["range"]["end"]));
                    var actions = new JsonArray();
                    void Offer(TjaCodeAction action)
                    {
                        var edits = new JsonArray();
                        foreach (var e in action.Edits) edits.Add(new JsonObject { ["range"] = Range(document, e.Span), ["newText"] = e.NewText });
                        actions.Add(new JsonObject
                        {
                            ["title"] = action.Title, ["kind"] = action.Fixes != null ? "quickfix" : "refactor.rewrite",
                            ["edit"] = new JsonObject { ["changes"] = new JsonObject { [uri] = edits } },
                        });
                    }
                    foreach (var d in document.Diagnostics(cancel))
                        if (d.Span.Start <= range.End && range.Start <= d.Span.End)
                            foreach (var action in TjaQuickFix.For(document, d)) Offer(action);
                    if (document.BlockAt(range.Start) is TjaDocumentBlock here)
                    {
                        if (TjaRefactor.Split(document, here.Course, here.Side, range.Start, cancel) is TjaCodeAction split) Offer(split);
                        if (TjaRefactor.Join(document, here.Course, here.Side, range.Start, cancel) is TjaCodeAction join) Offer(join);
                        if (TjaQuickFix.InlineBalloons(document, here.Course, here.Side, cancel) is TjaCodeAction inline) Offer(inline);
                    }
                    result = actions;
                }
                break;
            // Formatting; FormattingOptions may carry tjaResolution (slots per whole note).
            case "textDocument/formatting":
                handled = true;
                if (documents.TryGetValue((string)p["textDocument"]["uri"], out document))
                {
                    int resolution = p["options"]?["tjaResolution"] is JsonNode r ? (int)r : 0;
                    var edits = new JsonArray();
                    foreach (var e in TjaFormat.Edits(document, resolution, cancel)) edits.Add(new JsonObject { ["range"] = Range(document, e.Span), ["newText"] = e.NewText });
                    result = edits;
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
