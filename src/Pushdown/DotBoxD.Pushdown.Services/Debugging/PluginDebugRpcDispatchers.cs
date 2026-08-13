using System.Buffers;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;

namespace DotBoxD.Pushdown.Services;

/// <summary>Public, hand-writable RPC dispatcher for the host debug endpoint.</summary>
public sealed class PluginDebugControlRpcDispatcher : IServiceDispatcher, INonStreamingServiceDispatcher
{
    private const string MethodName = nameof(IPluginDebugControlRpcService.ExchangeAsync);
    private readonly IPluginDebugControlRpcService _service;

    public PluginDebugControlRpcDispatcher(IPluginDebugControlRpcService service) =>
        _service = service ?? throw new ArgumentNullException(nameof(service));

    public string ServiceName => "DotBoxD.PluginDebug.Control.v1";

    public async Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default)
    {
        if (!string.Equals(method, MethodName, StringComparison.Ordinal))
        {
            throw MissingMethod(ServiceName, method);
        }

        ct.ThrowIfCancellationRequested();
        var message = serializer.Deserialize<byte[]>(payload);
        var response = await _service.ExchangeAsync(message, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        serializer.Serialize(output, response);
    }

    private static ServiceNotFoundException MissingMethod(string service, string method) =>
        new(
            $"Method '{method}' not found on service '{service}'.",
            ServiceNotFoundException.NotFoundKind.Method);
}

/// <summary>Public, hand-writable RPC dispatcher for the reverse plugin debug-event endpoint.</summary>
public sealed class PluginDebugEventRpcDispatcher : IServiceDispatcher, INonStreamingServiceDispatcher
{
    private const string MethodName = nameof(IPluginDebugEventRpcService.PublishAsync);
    private readonly IPluginDebugEventRpcService _service;

    public PluginDebugEventRpcDispatcher(IPluginDebugEventRpcService service) =>
        _service = service ?? throw new ArgumentNullException(nameof(service));

    public string ServiceName => "DotBoxD.PluginDebug.Events.v1";

    public async Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default)
    {
        if (!string.Equals(method, MethodName, StringComparison.Ordinal))
        {
            throw new ServiceNotFoundException(
                $"Method '{method}' not found on service '{ServiceName}'.",
                ServiceNotFoundException.NotFoundKind.Method);
        }

        ct.ThrowIfCancellationRequested();
        var message = serializer.Deserialize<byte[]>(payload);
        await _service.PublishAsync(message, ct).ConfigureAwait(false);
    }
}
