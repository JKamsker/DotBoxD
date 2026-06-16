using System.Collections.Concurrent;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Plugins;

/// <summary>
/// In-process kernel RPC service convenience surface: register a batch kernel under a service contract,
/// then obtain a typed proxy that invokes it request/response. This is the in-process form of the
/// followup's <c>server.RegisterKernelRpcService&lt;TService, TKernel&gt;()</c> /
/// <c>server.KernelRpcService&lt;TService&gt;()</c>; over IPC the same shape is forwarded by a remote
/// facade (see the GameServer example).
/// </summary>
public sealed partial class PluginServer
{
    private readonly ConcurrentDictionary<Type, string> _rpcServices = new();

    /// <summary>
    /// Resolves <typeparamref name="TKernel"/>'s generated verified-IR package, installs it as a kernel
    /// RPC service, and binds the <typeparamref name="TService"/> contract to it for
    /// <see cref="RpcService{TService}"/>.
    /// </summary>
    public async ValueTask<string> RegisterRpcServiceAsync<TService, TKernel>(
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
        where TService : class
        where TKernel : class
    {
        var package = KernelPackageRegistry.Resolve(typeof(TKernel));
        var kernel = await InstallRpcAsync(package, policy, cancellationToken).ConfigureAwait(false);
        _rpcServices[typeof(TService)] = kernel.Manifest.PluginId;
        return kernel.Manifest.PluginId;
    }

    /// <summary>
    /// Returns a typed proxy implementing <typeparamref name="TService"/> whose calls run the bound batch
    /// kernel request/response (arguments and results are marshaled to and from the sandbox). Throws if no
    /// kernel was registered for the contract.
    /// </summary>
    public TService RpcService<TService>() where TService : class
    {
        var serviceType = typeof(TService);
        if (!_rpcServices.TryGetValue(serviceType, out var pluginId))
        {
            throw NoRpcServiceRegistered(serviceType);
        }

        if (!Kernels.TryGet(pluginId, out var kernel))
        {
            _rpcServices.TryRemove(serviceType, out _);
            throw NoRpcServiceRegistered(serviceType);
        }

        return KernelRpcServiceProxy.Create<TService>(kernel);
    }

    private void RemoveRpcServiceRegistrations(string pluginId)
    {
        foreach (var registration in _rpcServices)
        {
            if (string.Equals(registration.Value, pluginId, StringComparison.Ordinal))
            {
                _rpcServices.TryRemove(registration.Key, out _);
            }
        }
    }

    private static InvalidOperationException NoRpcServiceRegistered(Type serviceType)
        => new(
            $"No kernel RPC service is registered for '{serviceType}'. Call RegisterRpcServiceAsync first.");
}
