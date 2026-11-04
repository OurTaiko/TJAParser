using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace OurTaiko.LanguageServer;

// The TJA language server: LSP over a pair of streams. Documents are TjaDocument versions, one per
// URI; their diagnostics are read in the background after each change (a newer change cancels
// the read in progress) and published for the version they are of. Requests run in the background
// too and can be cancelled ($/cancelRequest). Language features are the library's (OurTaiko);
// this class only speaks the protocol.
public sealed partial class Server
{
    readonly JsonRpc rpc;
    readonly ConcurrentDictionary<string, TjaDocument> documents = new();
    readonly ConcurrentDictionary<string, CancellationTokenSource> diagnosing = new();
    readonly ConcurrentDictionary<string, CancellationTokenSource> requests = new();
    readonly ConcurrentDictionary<string, Task> background = new();
    // Milliseconds to wait after a change before reading the document's diagnostics, so a burst of
    // typing reads once (initializationOptions.debounceMs; 0 in tests).
    int debounce = 150;
    bool shutdown;

    public Server(Stream input, Stream output)
    {
        rpc = new JsonRpc(input, output);
    }

    // Serves until `exit` (or the end of the input); the exit code LSP asks for: 0 after shutdown.
    public async Task<int> RunAsync()
    {
        while (true)
        {
            var message = await rpc.ReadAsync();
            if (message == null) return shutdown ? 0 : 1;
            string method = (string)message["method"];
            var id = message["id"];
            var parameters = message["params"] as JsonObject;
            if (method == "exit") return shutdown ? 0 : 1;
            if (id == null) Notify(method, parameters);
            else Request(id.DeepClone(), method, parameters);
        }
    }

    // Notifications are handled in order: a document's changes must apply one after another.
    void Notify(string method, JsonObject p)
    {
        switch (method)
        {
            case "textDocument/didOpen":
            {
                var item = p["textDocument"].AsObject();
                string uri = (string)item["uri"];
                documents[uri] = new TjaDocument((string)item["text"], (int)item["version"]);
                Diagnose(uri);
                break;
            }
            case "textDocument/didChange":
            {
                string uri = (string)p["textDocument"]["uri"];
                int version = (int)p["textDocument"]["version"];
                if (!documents.TryGetValue(uri, out var document)) return;
                foreach (var change in p["contentChanges"].AsArray())
                {
                    if (change["range"] is JsonObject range)
                    {
                        int start = Offset(document, range["start"]), end = Offset(document, range["end"]);
                        document = document.WithChange(start, end - start, (string)change["text"], version);
                    }
                    else document = document.WithText((string)change["text"], version);
                }
                documents[uri] = document;
                Diagnose(uri);
                break;
            }
            case "textDocument/didClose":
            {
                string uri = (string)p["textDocument"]["uri"];
                documents.TryRemove(uri, out _);
                if (diagnosing.TryRemove(uri, out var running)) running.Cancel();
                _ = rpc.WriteAsync(Notification("textDocument/publishDiagnostics", new JsonObject { ["uri"] = uri, ["diagnostics"] = new JsonArray() }));
                break;
            }
            case "$/cancelRequest":
                if (requests.TryGetValue(p["id"].ToJsonString(), out var request)) request.Cancel();
                break;
        }
    }

    void Request(JsonNode id, string method, JsonObject p)
    {
        string key = id.ToJsonString();
        var cancel = new CancellationTokenSource();
        requests[key] = cancel;
        var task = Task.Run(async () =>
        {
            JsonObject response;
            try
            {
                var result = Handle(method, p, cancel.Token);
                response = new JsonObject { ["id"] = id, ["result"] = result };
            }
            catch (OperationCanceledException) { response = Error(id, -32800, "Request cancelled."); }
            catch (MethodNotFound) { response = Error(id, -32601, "Unknown method: " + method); }
            catch (Exception e) { response = Error(id, -32603, e.Message); }
            finally { requests.TryRemove(key, out _); }
            await rpc.WriteAsync(response);
        });
        background[key] = task;
        _ = task.ContinueWith(_ => background.TryRemove(key, out var _));
    }

    sealed class MethodNotFound : Exception { }

    JsonNode Handle(string method, JsonObject p, CancellationToken cancel)
    {
        switch (method)
        {
            case "initialize":
                if (p?["initializationOptions"]?["debounceMs"] is JsonNode d) debounce = (int)d;
                return new JsonObject
                {
                    ["capabilities"] = Capabilities(),
                    ["serverInfo"] = new JsonObject { ["name"] = "tja-language-server", ["version"] = ChartHash.Prefix.TrimEnd(':') },
                };
            case "shutdown":
                shutdown = true;
                return null;
            default:
                JsonNode result = null;
                bool handled = false;
                Features(method, p, cancel, ref result, ref handled);
                if (!handled) throw new MethodNotFound();
                return result;
        }
    }

    static JsonObject Capabilities() => new()
    {
        ["positionEncoding"] = "utf-16",
        // Incremental: each change is the range it replaces.
        ["textDocumentSync"] = new JsonObject { ["openClose"] = true, ["change"] = 2 },
    };

    // Requests of the language features (later steps add them): `handled` says the method is one.
    partial void Features(string method, JsonObject p, CancellationToken cancel, ref JsonNode result, ref bool handled);

    // Reads a document's diagnostics in the background and publishes them for its version, unless
    // a later change came first.
    void Diagnose(string uri)
    {
        var cancel = new CancellationTokenSource();
        if (diagnosing.TryGetValue(uri, out var earlier)) earlier.Cancel();
        diagnosing[uri] = cancel;
        var document = documents[uri];
        string key = "diagnose:" + uri + ":" + document.Version;
        var task = Task.Run(async () =>
        {
            try
            {
                if (debounce > 0) await Task.Delay(debounce, cancel.Token);
                var diagnostics = document.Diagnostics(cancel.Token);
                var array = new JsonArray();
                foreach (var d in diagnostics) array.Add(Diagnostic(document, d));
                cancel.Token.ThrowIfCancellationRequested();
                await rpc.WriteAsync(Notification("textDocument/publishDiagnostics",
                    new JsonObject { ["uri"] = uri, ["version"] = document.Version, ["diagnostics"] = array }));
            }
            catch (OperationCanceledException) { }
        });
        background[key] = task;
        _ = task.ContinueWith(_ => background.TryRemove(key, out var _));
    }

    static JsonObject Diagnostic(TjaDocument document, TjaDiagnostic d) => new()
    {
        ["range"] = Range(document, d.Span),
        ["severity"] = d.Severity switch { TjaSeverity.Error => 1, TjaSeverity.Warning => 2, _ => 3 },
        ["code"] = d.Code,
        ["source"] = "tja",
        ["message"] = d.Message,
    };

    // Waits for the work in the background (for tests).
    public Task Idle() => Task.WhenAll(background.Values.ToArray());

    // LSP positions: lines and UTF-16 characters from 0. TjaSourceText's lines count from 1.
    internal static int Offset(TjaDocument document, JsonNode position)
        => document.Lines.Offset((int)position["line"] + 1, (int)position["character"]);

    internal static JsonObject Position(TjaDocument document, int offset)
    {
        var (line, column) = document.Lines.Position(offset);
        return new JsonObject { ["line"] = line - 1, ["character"] = column };
    }

    internal static JsonObject Range(TjaDocument document, TextSpan span)
        => new() { ["start"] = Position(document, span.Start), ["end"] = Position(document, span.End) };

    static JsonObject Notification(string method, JsonObject p) => new() { ["method"] = method, ["params"] = p };

    static JsonObject Error(JsonNode id, int code, string message)
        => new() { ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };
}
