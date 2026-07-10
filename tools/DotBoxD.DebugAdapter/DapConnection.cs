using System.Text;
using System.Text.Json;

namespace DotBoxD.DebugAdapter;

internal sealed class DapConnection(Stream input, Stream output)
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _sequence;

    public async ValueTask<JsonDocument?> ReadAsync(CancellationToken cancellationToken)
    {
        var header = await ReadHeaderAsync(cancellationToken).ConfigureAwait(false);
        if (header is null)
        {
            return null;
        }

        var length = ParseContentLength(header);
        if (length <= 0 || length > 16 * 1024 * 1024)
        {
            throw new InvalidDataException("DAP Content-Length is outside the supported range.");
        }

        var payload = new byte[length];
        await input.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(payload);
    }

    public ValueTask RespondAsync(
        JsonElement request,
        bool success,
        object? body,
        string? message,
        CancellationToken cancellationToken) =>
        WriteAsync(
            new
            {
                seq = NextSequence(),
                type = "response",
                request_seq = request.GetProperty("seq").GetInt32(),
                success,
                command = request.GetProperty("command").GetString(),
                message,
                body
            },
            cancellationToken);

    public ValueTask EventAsync(string eventName, object? body, CancellationToken cancellationToken = default) =>
        WriteAsync(new { seq = NextSequence(), type = "event", @event = eventName, body }, cancellationToken);

    private async ValueTask WriteAsync(object message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, DapJson.Options);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async ValueTask<string?> ReadHeaderAsync(CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(128);
        while (bytes.Count < 64 * 1024)
        {
            var buffer = new byte[1];
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return bytes.Count == 0 ? null : throw new EndOfStreamException("DAP header ended unexpectedly.");
            }

            bytes.Add(buffer[0]);
            if (bytes.Count >= 4 &&
                bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n')
            {
                return Encoding.ASCII.GetString(bytes.ToArray());
            }
        }

        throw new InvalidDataException("DAP header is too large.");
    }

    private static int ParseContentLength(string header)
    {
        foreach (var line in header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator > 0 &&
                string.Equals(line[..separator], "Content-Length", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(line.AsSpan(separator + 1), out var length))
            {
                return length;
            }
        }

        throw new InvalidDataException("DAP Content-Length header is missing.");
    }

    private int NextSequence() => Interlocked.Increment(ref _sequence);
}

internal static class DapJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
