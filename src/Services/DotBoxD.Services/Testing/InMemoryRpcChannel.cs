using System.Threading.Channels;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Testing;

/// <summary>Creates deterministic in-memory channel pairs for consumer integration tests.</summary>
public static class InMemoryRpcChannel
{
    public static (IRpcChannel First, IRpcChannel Second) CreatePair(int capacity = 256)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        var firstInbound = CreateQueue(capacity);
        var secondInbound = CreateQueue(capacity);
        return (
            new Endpoint("memory://second", firstInbound, secondInbound.Writer),
            new Endpoint("memory://first", secondInbound, firstInbound.Writer));
    }

    private static Channel<Payload> CreateQueue(int capacity)
        => Channel.CreateBounded<Payload>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    private sealed class Endpoint(
        string remoteEndpoint,
        Channel<Payload> inbound,
        ChannelWriter<Payload> outbound) : IRpcChannel
    {
        private int _disposed;

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint { get; } = remoteEndpoint;

        public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(Endpoint));
            }
            var payload = Payload.Rent(data.Length);
            data.Span.CopyTo(payload.Memory.Span);
            try
            {
                await outbound.WriteAsync(payload, ct).ConfigureAwait(false);
            }
            catch
            {
                payload.Dispose();
                throw;
            }
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            while (await inbound.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                if (inbound.Reader.TryRead(out var payload))
                {
                    return payload;
                }
            }

            return Payload.Empty;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                outbound.TryComplete();
                inbound.Writer.TryComplete();
                while (inbound.Reader.TryRead(out var payload))
                {
                    payload.Dispose();
                }
            }

            return default;
        }
    }
}
