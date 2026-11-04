using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json.Nodes;
using OurTaiko;
using OurTaiko.LanguageServer;
using Xunit;

namespace OurTaiko.Tests;

// An LSP client for the tests: the server as a process (stdin and stdout) or in this process
// (over pipes). Messages from the server are collected as they come.
sealed class LspClient : IAsyncDisposable
{
    readonly JsonRpc rpc;
    readonly Process process;
    readonly Task<int> inProcess;
    readonly Task reading;
    readonly List<JsonObject> received = new();
    readonly SemaphoreSlim arrived = new(0);
    int nextId;

    LspClient(Stream fromServer, Stream toServer, Process process, Task<int> inProcess)
    {
        rpc = new JsonRpc(fromServer, toServer);
        this.process = process; this.inProcess = inProcess;
        reading = Task.Run(async () =>
        {
            while (await rpc.ReadAsync() is JsonObject message)
            {
                lock (received) received.Add(message);
                arrived.Release();
            }
        });
    }

    public static LspClient StartProcess()
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "tja-language-server.dll"));
        var process = Process.Start(start);
        return new LspClient(process.StandardOutput.BaseStream, process.StandardInput.BaseStream, process, null);
    }

    public static LspClient StartInProcess()
    {
        var toServer = new AnonymousPipeServerStream(PipeDirection.Out);
        var serverIn = new AnonymousPipeClientStream(PipeDirection.In, toServer.ClientSafePipeHandle);
        var fromServer = new AnonymousPipeServerStream(PipeDirection.In);
        var serverOut = new AnonymousPipeClientStream(PipeDirection.Out, fromServer.ClientSafePipeHandle);
        var server = new Server(serverIn, serverOut);
        return new LspClient(fromServer, toServer, null, Task.Run(server.RunAsync));
    }

    public async Task<JsonObject> Request(string method, JsonNode parameters = null)
    {
        int id = ++nextId;
        await rpc.WriteAsync(new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters });
        return await Wait(m => m["id"] is JsonNode i && i.GetValueKind() == System.Text.Json.JsonValueKind.Number && (int)i == id && m["method"] == null);
    }

    public Task Notify(string method, JsonNode parameters = null)
        => rpc.WriteAsync(new JsonObject { ["method"] = method, ["params"] = parameters });

    // The first message that matches, waiting for it (and taking it out of what was received).
    public async Task<JsonObject> Wait(Func<JsonObject, bool> match, int timeoutMs = 20000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            lock (received)
            {
                var found = received.FirstOrDefault(match);
                if (found != null) { received.Remove(found); return found; }
            }
            var left = until - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) throw new TimeoutException("No such message from the server.");
            await arrived.WaitAsync(left);
        }
    }

    // The latest diagnostics published for a URI at a version, waiting for them.
    public async Task<JsonArray> Diagnostics(string uri, int version)
        => (await Wait(m => (string)m["method"] == "textDocument/publishDiagnostics" && (string)m["params"]["uri"] == uri
            && (int?)m["params"]["version"] == version))["params"]["diagnostics"].AsArray();

    public async Task<int> Exit()
    {
        await Notify("exit");
        if (process != null)
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            return process.ExitCode;
        }
        return await inProcess.WaitAsync(TimeSpan.FromSeconds(20));
    }

    public async ValueTask DisposeAsync()
    {
        if (process != null && !process.HasExited) process.Kill();
        process?.Dispose();
        await Task.CompletedTask;
    }

    public static JsonObject Initialize(int debounceMs = 0) => new()
    {
        ["processId"] = null, ["rootUri"] = null, ["capabilities"] = new JsonObject(),
        ["initializationOptions"] = new JsonObject { ["debounceMs"] = debounceMs },
    };

    public static JsonObject Open(string uri, string text, int version = 1) => new()
    {
        ["textDocument"] = new JsonObject { ["uri"] = uri, ["languageId"] = "tja", ["version"] = version, ["text"] = text },
    };

    public static JsonObject Change(string uri, int version, int line, int character, int endLine, int endCharacter, string text) => new()
    {
        ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = version },
        ["contentChanges"] = new JsonArray(new JsonObject
        {
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject { ["line"] = line, ["character"] = character },
                ["end"] = new JsonObject { ["line"] = endLine, ["character"] = endCharacter },
            },
            ["text"] = text,
        }),
    };
}

// I2-1 (Documentation~/Parser-TODO.md): the language server answers an LSP client.
public class LanguageServerTests
{
    const string Uri = "file:///song.tja";
    const string Text = "BPM:120\nCOURSE:Oni\n#START\n#SCROLL abc\n1H10,\n#END\n";

    static string Summary(JsonArray diagnostics) => string.Join("; ", diagnostics.Select(d =>
        $"{d["code"]} {d["severity"]} {d["range"]["start"]["line"]}:{d["range"]["start"]["character"]}-{d["range"]["end"]["line"]}:{d["range"]["end"]["character"]}"));

    [Fact]
    public async Task AnEditorOpensAndEditsADocumentAndSeesItsDiagnostics()
    {
        await using var client = LspClient.StartProcess();
        var init = await client.Request("initialize", LspClient.Initialize());
        Assert.Equal(2, (int)init["result"]["capabilities"]["textDocumentSync"]["change"]);
        await client.Notify("initialized", new JsonObject());

        await client.Notify("textDocument/didOpen", LspClient.Open(Uri, Text));
        // The dropped #SCROLL (a warning) on line 3, the unknown note H (an error) on line 4.
        Assert.Equal("TJA2010 2 3:0-3:11; TJA1008 1 4:1-4:2", Summary(await client.Diagnostics(Uri, 1)));

        // An edit: `#FOO` before the note line, and the H made a 2.
        await client.Notify("textDocument/didChange", LspClient.Change(Uri, 2, 4, 0, 4, 0, "#FOO\n"));
        await client.Notify("textDocument/didChange", LspClient.Change(Uri, 3, 5, 1, 5, 2, "2"));
        Assert.Equal("TJA2010 2 3:0-3:11; TJA2009 2 4:0-4:4", Summary(await client.Diagnostics(Uri, 3)));

        Assert.Null((await client.Request("shutdown"))["result"]);
        Assert.Equal(0, await client.Exit());
    }

    [Fact]
    public async Task UnknownMethodsAreRefusedAndNoticesIgnored()
    {
        await using var client = LspClient.StartInProcess();
        await client.Request("initialize", LspClient.Initialize());
        await client.Notify("workspace/didChangeConfiguration", new JsonObject());
        var answer = await client.Request("textDocument/somethingElse", new JsonObject());
        Assert.Equal(-32601, (int)answer["error"]["code"]);
        await client.Request("shutdown");
        Assert.Equal(0, await client.Exit());
    }

    [Fact]
    public async Task OnlyTheLatestVersionIsDiagnosedAfterABurstOfTyping()
    {
        await using var client = LspClient.StartInProcess();
        await client.Request("initialize", LspClient.Initialize(debounceMs: 100));
        await client.Notify("textDocument/didOpen", LspClient.Open(Uri, Text));
        for (int v = 2; v <= 6; v++) await client.Notify("textDocument/didChange", LspClient.Change(Uri, v, 4, 0, 4, 0, "1"));
        var last = await client.Diagnostics(Uri, 6);
        Assert.Contains("TJA1008", Summary(last));
        // Versions 1 to 5 were superseded within the wait: none of them was published.
        await Task.Delay(300);
        await Assert.ThrowsAsync<TimeoutException>(() => client.Wait(m => (string)m["method"] == "textDocument/publishDiagnostics", 50));
        await client.Request("shutdown");
        Assert.Equal(0, await client.Exit());
    }

    [Fact]
    public async Task ExitWithoutShutdownIsAnError()
    {
        await using var client = LspClient.StartInProcess();
        await client.Request("initialize", LspClient.Initialize());
        Assert.Equal(1, await client.Exit());
    }

    [Fact]
    public async Task FramesCountBytesNotCharacters()
    {
        var a = new MemoryStream();
        var writer = new JsonRpc(Stream.Null, a);
        await writer.WriteAsync(new JsonObject { ["method"] = "x", ["params"] = new JsonObject { ["text"] = "太鼓の達人 🥁" } });
        await writer.WriteAsync(new JsonObject { ["method"] = "y" });
        a.Position = 0;
        var reader = new JsonRpc(a, Stream.Null);
        Assert.Equal("太鼓の達人 🥁", (string)(await reader.ReadAsync())["params"]["text"]);
        Assert.Equal("y", (string)(await reader.ReadAsync())["method"]);
        Assert.Null(await reader.ReadAsync());
    }

    // A problem every block shares (here BPM, written before them) is one diagnostic.
    [Fact]
    public void ADocumentsDiagnosticsAreEachBlocksOnce()
    {
        var document = new TjaDocument("BPM:x\nCOURSE:Easy\n#START\n1,\n#END\nCOURSE:Oni\n#START\n#FOO\n1,\n#END\n");
        Assert.Equal(new[] { "TJA1003 1", "TJA2009 8" }, document.Diagnostics().Select(d => d.Code + " " + d.Line));
    }
}
