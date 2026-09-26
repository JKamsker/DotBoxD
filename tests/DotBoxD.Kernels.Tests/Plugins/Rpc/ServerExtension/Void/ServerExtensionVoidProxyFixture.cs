using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

internal static class ServerExtensionVoidProxyFixture
{
    internal static readonly SourceSpan Span = new(1, 1);

    internal static PluginPackage UnitPackage()
        => Package(SandboxType.Unit, new LiteralExpression(SandboxValue.Unit, Span));

    internal static PluginPackage Package(SandboxType returnType, Expression result, bool isAsync = false)
    {
        var requests = isAsync ? new[] { new CapabilityRequest(RuntimeCapabilityIds.Async, "test await") } : [];
        var function = new SandboxFunction(
            "Invoke", true, [], returnType, [new ReturnStatement(result, Span)]);
        var module = new SandboxModule(
            "void-proxy", SemVersion.One, SemVersion.One, requests, [function],
            new Dictionary<string, string> { ["pluginId"] = "void-proxy", ["kernel"] = nameof(IVoidServerExtension) });
        var manifest = new PluginManifest(
            "void-proxy", nameof(IVoidServerExtension), ExecutionMode.Auto,
            isAsync ? ["Cpu", "Concurrency"] : ["Cpu"], [], [])
        {
            RpcEntrypoint = "Invoke",
            RequiredCapabilities = isAsync ? [RuntimeCapabilityIds.Async] : []
        };
        return PluginPackage.Create(manifest, module, new KernelEntrypoints("Invoke", "Invoke"));
    }
}

public interface IVoidServerExtension
{
    void Invoke();
}
