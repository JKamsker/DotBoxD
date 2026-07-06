using DotBoxD.Services.Buffers;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Transport.NamedPipeTransportTestSupport;

namespace DotBoxD.Services.Tests.Coverage.Transport;

public sealed class NamedPipeClientTransportCancellationTests
{
    [Fact]
    public async Task AcceptAsync_Throws_WhenSecondCallTokenCancelled()
    {
        await using var channel = new ScriptedConnection();
        await using var server = new SingleConnectionServerTransport(channel);
        await server.StartAsync();

        _ = await server.AcceptAsync().WaitAsync(TransportTimeout);

        using var cts = new CancellationTokenSource();
        var secondAccept = server.AcceptAsync(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => secondAccept.WaitAsync(TransportTimeout));
    }

    [Fact]
    public async Task AcceptAsync_Unblocks_WhenDisposedWhilePending()
    {
        var channel = new TrackingChannel();
        var server = new SingleConnectionServerTransport(channel, ownsConnection: true);
        await server.StartAsync();
        _ = await server.AcceptAsync().WaitAsync(TransportTimeout);

        var secondAccept = server.AcceptAsync();
        await server.DisposeAsync();

        // Dispose sets the stop result; the parked accept resumes and throws (token not cancelled).
        await Assert.ThrowsAsync<OperationCanceledException>(() => secondAccept.WaitAsync(TransportTimeout));
        Assert.Equal(1, channel.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotDisposeConnection_WhenNotOwned()
    {
        var channel = new TrackingChannel();
        var server = new SingleConnectionServerTransport(channel, ownsConnection: false);
        await server.StartAsync();

        await server.DisposeAsync();

        Assert.Equal(0, channel.DisposeCount);
        await channel.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent_WhenOwned()
    {
        var channel = new TrackingChannel();
        var server = new SingleConnectionServerTransport(channel, ownsConnection: true);
        await server.StartAsync();

        await server.DisposeAsync();
        await server.DisposeAsync();

        Assert.Equal(1, channel.DisposeCount);
    }
}

/// <summary>
/// Minimal <see cref="IRpcChannel"/> that only tracks how many times it was disposed, so ownership
/// semantics of the single-connection transports can be asserted without a real link.
/// </summary>
internal sealed class TrackingChannel : IRpcChannel
{
    private int _disposeCount;

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public bool IsConnected => DisposeCount == 0;

    public string RemoteEndpoint => "tracking://channel";

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<Payload> ReceiveAsync(CancellationToken ct = default) =>
        Task.FromResult(Payload.Empty);

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCount);
        return default;
    }

}
