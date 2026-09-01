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
    private readonly Func<ReadOnlyMemory<byte>, int, CancellationToken, ValueTask<ReadOnlyMemory<byte>>> _transformSend;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private int _receives;
    private int _sends;

    public FaultInjectingRpcChannel(
        IRpcChannel inner,
        Func<RpcChannelOperation, int, CancellationToken, ValueTask> beforeOperation)
        : this(inner, beforeOperation, static (data, _, _) => new ValueTask<ReadOnlyMemory<byte>>(data))
    {
    }

    public FaultInjectingRpcChannel(
        IRpcChannel inner,
        Func<RpcChannelOperation, int, CancellationToken, ValueTask> beforeOperation,
        Func<ReadOnlyMemory<byte>, int, CancellationToken, ValueTask<ReadOnlyMemory<byte>>> transformSend)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _beforeOperation = beforeOperation ?? throw new ArgumentNullException(nameof(beforeOperation));
        _transformSend = transformSend ?? throw new ArgumentNullException(nameof(transformSend));
    }

    public bool IsConnected => _inner.IsConnected;

    public string RemoteEndpoint => _inner.RemoteEndpoint;

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        var operation = Interlocked.Increment(ref _sends);
        await _beforeOperation(RpcChannelOperation.Send, operation, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var transformed = await _transformSend(data, operation, ct).ConfigureAwait(false);
        await _inner.SendAsync(transformed, ct).ConfigureAwait(false);
    }

    public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
    {
        var operation = Interlocked.Increment(ref _receives);
        await _beforeOperation(RpcChannelOperation.Receive, operation, ct).ConfigureAwait(false);
        return await _inner.ReceiveAsync(ct).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource<bool> completion;
        Task disposeTask;

        lock (_disposeGate)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            disposeTask = completion.Task;
            _disposeTask = disposeTask;
        }

        _ = CompleteDisposeAsync(completion);
        return new ValueTask(disposeTask);
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource<bool> completion)
    {
        try
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            completion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }
}
