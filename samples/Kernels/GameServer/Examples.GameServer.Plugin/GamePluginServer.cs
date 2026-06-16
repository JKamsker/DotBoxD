namespace DotBoxD.Kernels.Game.Plugin;

/// <summary>
/// The entire facade the plugin dev writes — just the shell. From the <c>: IGameWorldAccess</c> base the
/// DotBoxD generator emits, plugin-side:
/// <list type="bullet">
///   <item>the RPC-proxy implementation of <c>IGameWorldAccess</c> (the <c>Monsters</c>/<c>Entities</c>
///   controls and their methods marshal over IPC) — generated "like any other <c>[DotBoxDService]</c>";</item>
///   <item>the mixed-in <c>IPluginServer&lt;IGameWorldAccess&gt;</c> half — <c>StartAsync</c> (connect),
///   <c>RunAsync</c>, <c>HoldUntilShutdownAsync</c>, <c>InvokeAsync</c>, the started-gate, <c>Dispose</c>;</item>
///   <item>the install verbs <c>Replace</c>/<c>Extend</c>/<c>Get</c> (ship verified IR through the
///   control-plane);</item>
///   <item>a <c>GamePluginServerBuilder</c> (<c>FromPipeName</c>/<c>FromConnection</c>, sync <c>Build()</c>).</item>
/// </list>
/// So the generated class is <c>GamePluginServer : IGameWorldAccess, IPluginServer&lt;IGameWorldAccess&gt;</c>.
/// </summary>
[GeneratePluginServer]
public partial class GamePluginServer : IGameWorldAccess
{
    // OPTIONAL extension hook. The generator declares `partial void OnConfigured();` and calls it after the
    // controls are wired (inside StartAsync). Implement it for custom setup — or delete it; with no body
    // the call compiles away.
    partial void OnConfigured()
        => Console.WriteLine("[plugin] OnConfigured: custom plugin wiring ran.");
}

/// <summary>Plain dev-authored capture object for the explicit capture-bag <c>InvokeAsync</c> overload.</summary>
public sealed class MonsterProbeCapture
{
    public string MonsterId { get; set; } = string.Empty;
    public int LastHealth { get; set; }
}
