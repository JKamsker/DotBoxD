using DotBoxD.Services.Buffers;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Testing;

/// <summary>Operation kinds exposed to deterministic channel fault plans.</summary>
public enum RpcChannelOperation
{
    Send,
    Receive
}

/// <summary>
/// Wraps a channel and invokes a deterministic hook before each send or receive. The hook may delay,
/// cancel, disconnect, or throw to exercise consumer recovery paths.
/// </summary>
public sealed class FaultInjectingRpcChannel : IRpcChannel
{
    private readonly IRpcChannel _inner;
    private readonly Func<RpcChannelOperation, int, CancellationToken, ValueTask> _beforeOperation;
    private int _receives;
    private int _sends;

    public FaultInjectingRpcChannel(
        IRpcChannel inner,
        Func<RpcChannelOperation, int, CancellationToken, ValueTask> beforeOperation)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _beforeOperation = beforeOperation ?? throw new ArgumentNullException(nameof(beforeOperation));
    }

    public bool IsConnected => _inner.IsConnected;

    public string RemoteEndpoint => _inner.RemoteEndpoint;

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        var operation = Interlocked.Increment(ref _sends);
        await _beforeOperation(RpcChannelOperation.Send, operation, ct).ConfigureAwait(false);
        await _inner.SendAsync(data, ct).ConfigureAwait(false);
    }

    public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
    {
        var operation = Interlocked.Increment(ref _receives);
        await _beforeOperation(RpcChannelOperation.Receive, operation, ct).ConfigureAwait(false);
        return await _inner.ReceiveAsync(ct).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
