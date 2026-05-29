using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Serialization;

namespace ShaRPC.Core.Server;

/// <summary>
/// Interface for generated service dispatchers that route incoming requests to the
/// appropriate service method. Root-service dispatchers use <see cref="DispatchAsync"/>;
/// sub-service dispatchers also implement <see cref="DispatchOnInstanceAsync"/> to route
/// calls to a particular registered instance.
/// </summary>
public interface IServiceDispatcher
{
    /// <summary>The RPC service name this dispatcher handles.</summary>
    string ServiceName { get; }

    /// <summary>
    /// Dispatches a singleton-service request to the appropriate method and returns the
    /// serialized result. <paramref name="registry"/> is the per-connection instance
    /// registry — dispatchers ignore it unless the method returns a sub-service interface,
    /// in which case they register the returned instance and serialize a
    /// <see cref="ShaRPC.Core.Protocol.ServiceHandle"/>.
    /// </summary>
    Task<Payload> DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        CancellationToken ct = default);

    /// <summary>
    /// Dispatches a call to a specific server-side instance previously registered with
    /// <see cref="IInstanceRegistry.Register"/>. Default implementation throws — the
    /// generator only emits an override for service dispatchers that may be reached as
    /// sub-services.
    /// </summary>
    Task<Payload> DispatchOnInstanceAsync(
        string instanceId,
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        CancellationToken ct = default) =>
        throw new ShaRpcNotFoundException(
            $"Service '{ServiceName}' does not support instance-scoped dispatch.");
}
