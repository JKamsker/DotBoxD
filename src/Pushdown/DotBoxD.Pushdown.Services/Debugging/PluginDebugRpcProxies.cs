using DotBoxD.Services.Server;

namespace DotBoxD.Pushdown.Services;

/// <summary>Public, hand-writable RPC proxy for the host debug endpoint.</summary>
public sealed class PluginDebugControlRpcProxy : IPluginDebugControlRpcService
{
    private const string ServiceName = "DotBoxD.PluginDebug.Control.v1";
    private readonly IRpcInvoker _invoker;

    public PluginDebugControlRpcProxy(IRpcInvoker invoker) =>
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));

    public ValueTask<byte[]> ExchangeAsync(byte[] message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            return _invoker.InvokeValueAsync<byte[], byte[]>(
                ServiceName,
                nameof(ExchangeAsync),
                message,
                cancellationToken);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            return new ValueTask<byte[]>(Task.FromCanceled<byte[]>(ex.CancellationToken));
        }
        catch (Exception ex)
        {
            return new ValueTask<byte[]>(Task.FromException<byte[]>(ex));
        }
    }
}

/// <summary>Public, hand-writable RPC proxy for the reverse plugin debug-event endpoint.</summary>
public sealed class PluginDebugEventRpcProxy : IPluginDebugEventRpcService
{
    private const string ServiceName = "DotBoxD.PluginDebug.Events.v1";
    private readonly IRpcInvoker _invoker;

    public PluginDebugEventRpcProxy(IRpcInvoker invoker) =>
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));

    public ValueTask PublishAsync(byte[] message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            return _invoker.InvokeValueAsync(ServiceName, nameof(PublishAsync), message, cancellationToken);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            return new ValueTask(Task.FromCanceled(ex.CancellationToken));
        }
        catch (Exception ex)
        {
            return new ValueTask(Task.FromException(ex));
        }
    }
}
