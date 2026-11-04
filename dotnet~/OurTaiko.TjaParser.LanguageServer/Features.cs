using System.Text.Json.Nodes;

namespace OurTaiko.LanguageServer;

// The language features over LSP: each asks the library (OurTaiko) and turns the answer into the
// protocol's terms (ranges in lines and UTF-16 characters from 0).
public sealed partial class Server
{
    static partial void FeatureCapabilities(JsonObject capabilities)
    {
        capabilities["completionProvider"] = new JsonObject { ["triggerCharacters"] = new JsonArray("#", ":", " ") };
    }

    partial void Features(string method, JsonObject p, CancellationToken cancel, ref JsonNode result, ref bool handled)
    {
        switch (method)
        {
            case "textDocument/completion":
                handled = true;
                result = At(p, out var document, out int offset) ? Completion(document, offset, cancel) : null;
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
