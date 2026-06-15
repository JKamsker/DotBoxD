namespace DotBoxD.Plugins;

/// <summary>
/// Client-side transport used by generated kernel RPC service proxies. The payload is DotBoxD's compact
/// kernel RPC value IR encoded by <see cref="KernelRpcBinaryCodec"/>, so transports can carry it as an
/// ordinary binary IPC argument without knowing the plugin-owned service contract.
/// </summary>
public interface IKernelRpcWireClient
{
    ValueTask<byte[]> InvokeKernelRpcAsync(
        string pluginId,
        byte[] arguments,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Adds service-contract lookup for generated domain-style kernel RPC client extensions.
/// </summary>
public interface IKernelRpcClientRegistry : IKernelRpcWireClient
{
    string PluginId<TService>()
        where TService : class;
}
