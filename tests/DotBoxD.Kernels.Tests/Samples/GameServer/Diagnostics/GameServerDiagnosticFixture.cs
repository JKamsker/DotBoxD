using System.Reflection;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

internal sealed class GameServerDiagnosticFixture : IDisposable
{
    private readonly PluginServer _server;
    private readonly PluginSession _session;
    private readonly object _service;

    public GameServerDiagnosticFixture()
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../../samples/GameServer/Examples.GameServer.Server/bin",
            configuration, "net10.0/Examples.GameServer.Server.dll"));
        var assembly = Assembly.LoadFrom(path);
        var sink = (IPluginMessageSink)Create(assembly, "Simulation.GameCommandSink");
        _server = PluginServer.Create(sink);
        _session = _server.CreateSession();
        var world = assembly.GetType("DotBoxD.Kernels.Game.Server.Simulation.GameWorld", throwOnError: true)!
            .GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [_server.Hooks, _server.Subscriptions])!;
        _service = Create(assembly, "Ipc.GamePluginControlService", _server, _session, sink, world);
    }

    public ValueTask<string> InstallAsync(PluginPackage package, CancellationToken cancellationToken = default)
        => (ValueTask<string>)_service.GetType().GetMethod("InstallServerExtensionAsync")!
            .Invoke(_service, [PluginPackageJsonSerializer.Export(package), cancellationToken])!;

    public ValueTask<byte[]> InvokeAsync(string pluginId, byte[] arguments, CancellationToken cancellationToken = default)
        => (ValueTask<byte[]>)_service.GetType().GetMethod("InvokeServerExtensionAsync")!
            .Invoke(_service, [pluginId, arguments, cancellationToken])!;

    public void CloseSession() => _session.Dispose();

    public void Dispose()
    {
        _session.Dispose();
        _server.Dispose();
    }

    public static PluginPackage Package()
    {
        const string id = "diagnostic-test";
        var span = new SourceSpan(1, 1);
        var function = new SandboxFunction(
            "Invoke", true, [], SandboxType.Unit,
            [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, span), span)]);
        var module = new SandboxModule(
            id, SemVersion.One, SemVersion.One, [], [function],
            new Dictionary<string, string> { ["pluginId"] = id, ["kernel"] = id });
        var manifest = new PluginManifest(id, id, ExecutionMode.Auto, ["Cpu"], [], [])
        {
            RpcEntrypoint = "Invoke"
        };
        return PluginPackage.Create(manifest, module, new KernelEntrypoints("Invoke", "Invoke"));
    }

    private static object Create(Assembly assembly, string name, params object[] arguments)
        => Activator.CreateInstance(
            assembly.GetType("DotBoxD.Kernels.Game.Server." + name, throwOnError: true)!,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null, arguments, culture: null)!;
}
