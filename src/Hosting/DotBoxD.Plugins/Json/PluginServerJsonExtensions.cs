using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins.Json;

public static class PluginServerJsonExtensions
{
    public static ValueTask<InstalledKernel> InstallJsonAsync(
        this PluginServer server,
        string json,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        cancellationToken.ThrowIfCancellationRequested();
        var package = PluginPackageJsonSerializer.Import(json);
        return package.Manifest.RpcEntrypoint is not null
            ? server.InstallServerExtensionAsync(package, policy, cancellationToken)
            : server.InstallAsync(package, policy, cancellationToken);
    }

    public static ValueTask<InstalledKernel> InstallJsonAsync(
        this PluginSession session,
        string json,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();
        var package = PluginPackageJsonSerializer.Import(json);
        return package.Manifest.RpcEntrypoint is not null
            ? session.InstallServerExtensionAsync(package, policy, cancellationToken)
            : session.InstallAsync(package, policy, cancellationToken);
    }
}
