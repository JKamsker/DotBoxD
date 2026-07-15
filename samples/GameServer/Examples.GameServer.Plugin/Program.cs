using DotBoxD.Kernels.Game.Plugin.Authoring;
using DotBoxD.Kernels.Game.Plugin.Kernels;
using DotBoxD.Kernels.Game.Server.Abstractions.Events;
using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Peer;

namespace DotBoxD.Kernels.Game.Plugin;

/// <summary>
/// The golden path, from the plugin dev's seat. The server implements <c>IGameWorldAccess</c>; the plugin
/// holds a generated RPC proxy of the same interface (that proxy IS <c>server</c>); kernels get it injected.
/// One surface, three call sites.
///
/// <para><b>Which verb when:</b></para>
/// <list type="bullet">
///   <item><c>Setup(s =&gt; s.Hooks.On&lt;TEvent&gt;().Use&lt;TKernel&gt;())</c> — record awaited decision logic;
///   <c>StartAsync()</c> ships and wires the kernel.</item>
///   <item><c>Setup(s =&gt; s.Subscriptions.On&lt;TEvent&gt;().Use&lt;TKernel&gt;())</c> — record fire-and-forget
///   notifications for replay at <c>StartAsync()</c>.</item>
///   <item><c>server.Hooks.On&lt;TEvent&gt;()</c> and <c>server.Subscriptions.On&lt;TEvent&gt;()</c> — install
///   additional decision hooks or fire-and-forget notifications after <c>StartAsync()</c>.</item>
///   <item><c>.Where(...).Select(...).Run(...)</c> — install inline remote chains at runtime; filters,
///   projections, and <c>Run</c> are lowered to verified IR.</item>
///   <item><c>Setup(s =&gt; s.Monsters.Extend&lt;TKernel&gt;())</c> — record a <c>[ServerExtension]</c>; grafts a
///   method onto the control (batch) or onto each <c>IMonster</c> handle (per-instance).</item>
///   <item><c>Monsters.Get(id)</c> — a runtime scoped handle; calls on it omit the id.</item>
///   <item><c>Get&lt;TKernel&gt;()</c> — tune an installed kernel's live settings.</item>
///   <item><c>InvokeAsync(...)</c> — a throwaway server-side probe (see <c>AdvancedUsage</c>).</item>
/// </list>
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        string pipeName;
        try
        {
            pipeName = GamePluginServerHost.PipeNameFromArgs(args);   // throws ArgumentException on misuse
        }
        catch (ArgumentException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            return 1;
        }

        Console.WriteLine($"[plugin] connecting to server pipe '{pipeName}'...");

        await using var debugBridge = KernelDebuggingRequested()
            ? PluginDebugBridge.Start(new PluginDebugBridgeOptions
            {
                // Continuous debug rounds make late attachment safe and keep the compound launch non-blocking.
                WaitForDebuggerBeforeInstall = false
            })
            : null;
        if (debugBridge is not null)
        {
            Console.WriteLine($"[plugin] kernel debug bridge ready for PID {debugBridge.Descriptor.ProcessId}.");
        }

        var transportOptions = new NamedPipeTransportOptions { FrameReadIdleTimeout = Timeout.InfiniteTimeSpan };
        var connectionOptions = new RpcPeerOptions { RequestTimeout = Timeout.InfiniteTimeSpan };
        var builder = debugBridge is null
            ? GamePluginServerBuilder.FromPipeName(pipeName, transportOptions, connectionOptions)
            : GamePluginServerBuilder.FromPipeNameWithKernelDebugging(
                pipeName,
                debugBridge,
                transportOptions,
                connectionOptions);
        using IGameWorldServer server = builder
            .Setup(s =>
            {
                // Build() is sync and does no I/O; StartAsync() ships the recorded IR.
                s.Hooks.On<MonsterAggroEvent>().Use<GuardianKernel>();
                s.Subscriptions.On<AttackEvent>().Use<RetaliationKernel>();

                s.Monsters.Extend<MonsterKillerKernel>();        // grafts onto IMonsterControl (batch)
                s.Monsters.Extend<RangeMonsterKillerKernel>();   // batch with a value-object query parameter
                s.Monsters.Extend<BlinkKernel>();                // grafts onto IMonster handles (per-instance)
            })
            .Build();
        await server.StartAsync();

        ConfigureRuntimeHooks(server);

        // One direct domain call via a scoped handle — the id is captured by Get(id), so KillAsync omits it.
        var killed = await server.Monsters.Get("monster-4").KillAsync();
        Console.WriteLine($"[plugin] Monsters.Get(monster-4).KillAsync() => {killed}.");

        // Tune a replaced kernel's live settings — strongly typed member setters, one atomic batch. Only
        // [LiveSetting] members are settable; ApplyAsync ships it (a chain without ApplyAsync warns).
        await server.Get<GuardianKernel>()
            .Set(k => k.CalmStrength, 35)
            .Set(k => k.AggroRange, 6)
            .ApplyAsync(atomic: true);

        // The advanced surface (handles, server-extension calls, InvokeAsync probes) lives in its own file.
        // The Rider E2E harness skips it so Guardian breakpoint coverage has no dependency on unrelated demos.
        if (PluginLaunchMode.RunAdvancedUsage)
        {
            await AdvancedUsage.RunAsync(server);
        }

        Console.WriteLine("[plugin] kernels live; holding until server completes...");
        await server.HoldUntilShutdownAsync();

        return 0;
    }

    private static bool KernelDebuggingRequested()
        => string.Equals(Environment.GetEnvironmentVariable("DOTBOXD_KERNEL_DEBUG"), "1", StringComparison.Ordinal);

    internal static void ConfigureRuntimeHooks(IGameWorldServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        server.Hooks.On<MonsterAggroEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.MonsterId)
            .Run((monsterId, ctx) => ctx.Messages.Send(monsterId, "calm:inline"));

        server.Hooks.On<MonsterAggroEvent>().Run(
            (e, ctx) => ctx.Messages.Send(e.MonsterId, "observe:inline"));

        // PlayerTargetContext opts all eligible public instance methods into host binding defaults. The plugin
        // calls IsBelowLevel like an ordinary SDK helper; DotBoxD lowers the receiver plus argument into a
        // capability-gated server call. LocalLabel is public too, but [HostBindingIgnore] keeps it local-only.
        server.Hooks.On<PlayerTargetedEvent>()
            .Where(e => e.Player.IsBelowLevel(3))
            .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm:" + e.PlayerId + ":10"));

        server.Subscriptions.On<AttackEvent>()
            .Where(e => e.Damage >= 5)
            .Select(e => e.AttackerId)
            .Run((attackerId, ctx) => ctx.Messages.Send(attackerId, "taunt:inline"));

        // Indexed subscription: both .Where leaves compare an [EventIndexKey] property to a constant, so the
        // lowered chain ships index metadata (AttackerId == "player-1", Damage >= 5) on the manifest. The
        // host registers it into equality/range buckets and prefilters events before the verified IR runs —
        // see the "[server] registered indexed ..." diagnostics. The verified predicate still executes as the
        // correctness fallback for events the index lets through.
        server.Subscriptions.On<AttackEvent>()
            .Where(e => e.AttackerId == "player-1" && e.Damage >= 5)
            .Select(e => e.TargetId)
            .Run((targetId, ctx) => ctx.Messages.Send(targetId, "indexed-taunt:inline"));
    }
}
