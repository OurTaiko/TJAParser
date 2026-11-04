using System.Text;
using System.Text.Json.Nodes;

namespace OurTaiko.LanguageServer;

// JSON-RPC messages framed as LSP frames them: `Content-Length: n` headers, a blank line, n bytes
// of UTF-8 JSON.
public sealed class JsonRpc
{
    readonly Stream input, output;
    readonly SemaphoreSlim writing = new(1, 1);

    public JsonRpc(Stream input, Stream output)
    {
        this.input = input; this.output = output;
    }

    // The next message, or null at the end of the input.
    public async Task<JsonObject> ReadAsync(CancellationToken cancel = default)
    {
        int length = -1;
        while (true)
        {
            string header = await ReadHeaderLineAsync(cancel);
            if (header == null) return null;
            if (header.Length == 0) break;
            int colon = header.IndexOf(':');
            if (colon > 0 && header.Substring(0, colon).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                length = int.Parse(header.Substring(colon + 1).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }
        if (length < 0) throw new InvalidDataException("A message without Content-Length.");
        var body = new byte[length];
        for (int read = 0; read < length;)
        {
            int n = await input.ReadAsync(body.AsMemory(read, length - read), cancel);
            if (n == 0) return null;
            read += n;
        }
        return JsonNode.Parse(body)?.AsObject();
    }

    // A header line without its CRLF; null at the end of the input.
    async Task<string> ReadHeaderLineAsync(CancellationToken cancel)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            int n = await input.ReadAsync(one, cancel);
            if (n == 0) return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            if (one[0] == '\n')
            {
                if (bytes.Count > 0 && bytes[^1] == '\r') bytes.RemoveAt(bytes.Count - 1);
                return Encoding.ASCII.GetString(bytes.ToArray());
            }
            bytes.Add(one[0]);
        }
    }

    public async Task WriteAsync(JsonObject message)
    {
        message["jsonrpc"] = "2.0";
        byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await writing.WaitAsync();
        try
        {
            await output.WriteAsync(header);
            await output.WriteAsync(body);
            await output.FlushAsync();
        }
        finally { writing.Release(); }
    }
}
