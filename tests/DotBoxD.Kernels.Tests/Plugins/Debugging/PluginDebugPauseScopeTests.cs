using System.Text.Json;
using System.Threading.Channels;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Kernels.Tests.Plugins.Rpc;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Debugging;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging;

public sealed class PluginDebugPauseScopeTests
{
    [Fact]
    public async Task Plugin_session_pause_gates_owned_dispatch_only_and_never_exposes_foreign_runs()
    {
        var events = new EventQueue();
        using var server = DebugServer(KernelDebugPauseScope.PluginSession);
        using var owner = server.CreateSession();
        using var foreignOwner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(events);
        var package = FireDamagePluginPackage.Create();
        var breakpoint = StatementNode(package);
        _ = await SuccessAsync(debug, PluginDebugCommands.SetBreakpoints, new
        {
            pluginId = package.Manifest.PluginId,
            nodeIds = new[] { breakpoint.Value }
        });
        _ = await SuccessAsync(debug, PluginDebugCommands.Attach);
        var owned = await owner.InstallAsync(package);
        var foreign = await foreignOwner.InstallAsync(WithPluginId(package, "foreign-fire-damage"));

        var stoppedRun = ExecuteAsync(owned, "owner-1");
        var stopped = await events.NextAsync();
        var queuedOwnedRun = ExecuteAsync(owned, "owner-2");
        var foreignRun = ExecuteAsync(foreign, "foreign");

        Assert.True(await foreignRun.WaitAsync(TimeSpan.FromSeconds(2)));
        await Task.Delay(30);
        Assert.False(stoppedRun.IsCompleted);
        Assert.False(queuedOwnedRun.IsCompleted);
        var threads = await SuccessAsync(debug, PluginDebugCommands.Threads);
        Assert.Single(threads.GetProperty("threads").EnumerateArray());

        _ = await SuccessAsync(debug, PluginDebugCommands.SetBreakpoints, new
        {
            pluginId = package.Manifest.PluginId,
            nodeIds = Array.Empty<string>()
        });
        _ = await SuccessAsync(debug, PluginDebugCommands.Continue, new
        {
            runId = stopped.GetProperty("runId").GetString()
        });

        Assert.True(await stoppedRun);
        Assert.True(await queuedOwnedRun);
        Assert.Equal(1, events.Count);
    }

    [Fact]
    public async Task Execution_pause_tracks_concurrent_stopped_runs_as_independent_threads()
    {
        var events = new EventQueue();
        using var server = DebugServer(KernelDebugPauseScope.Execution);
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(events);
        var package = FireDamagePluginPackage.Create();
        var breakpoint = StatementNode(package);
        _ = await SuccessAsync(debug, PluginDebugCommands.SetBreakpoints, new
        {
            pluginId = package.Manifest.PluginId,
            nodeIds = new[] { breakpoint.Value }
        });
        var secondPackage = WithPluginId(package, "second-fire-damage");
        _ = await SuccessAsync(debug, PluginDebugCommands.SetBreakpoints, new
        {
            pluginId = secondPackage.Manifest.PluginId,
            nodeIds = new[] { breakpoint.Value }
        });
        _ = await SuccessAsync(debug, PluginDebugCommands.Attach);
        var firstKernel = await owner.InstallAsync(package);
        var secondKernel = await owner.InstallAsync(secondPackage);

        var firstRun = ExecuteAsync(firstKernel, "first");
        var secondRun = ExecuteAsync(secondKernel, "second");
        var firstStop = await events.NextAsync();
        var secondStop = await events.NextAsync();

        var runIds = new[]
        {
            firstStop.GetProperty("runId").GetString()!,
            secondStop.GetProperty("runId").GetString()!
        };
        Assert.Equal(2, runIds.Distinct(StringComparer.Ordinal).Count());
        var threads = await SuccessAsync(debug, PluginDebugCommands.Threads);
        Assert.Equal(2, threads.GetProperty("threads").GetArrayLength());

        foreach (var runId in runIds)
        {
            _ = await SuccessAsync(debug, PluginDebugCommands.Continue, new { runId });
        }

        Assert.True(await firstRun);
        Assert.True(await secondRun);
    }

    [Fact]
    public async Task Same_owner_hot_replacement_is_debuggable_without_reattaching()
    {
        var events = new EventQueue();
        using var server = DebugServer(KernelDebugPauseScope.Execution);
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(events);
        var package = FireDamagePluginPackage.Create();
        var breakpoint = StatementNode(package);
        _ = await SuccessAsync(debug, PluginDebugCommands.SetBreakpoints, new
        {
            pluginId = package.Manifest.PluginId,
            nodeIds = new[] { breakpoint.Value }
        });
        _ = await SuccessAsync(debug, PluginDebugCommands.Attach);
        var original = await owner.InstallAsync(package);

        var replacement = await owner.InstallAsync(package);
        Assert.True((bool)original.IsRevoked);
        Assert.False((bool)replacement.IsRevoked);

        var run = ExecuteAsync(replacement, "replacement");
        var stopped = await events.NextAsync();
        _ = await SuccessAsync(debug, PluginDebugCommands.Continue, new
        {
            runId = stopped.GetProperty("runId").GetString()
        });

        Assert.True(await run);
        Assert.True(debug.IsAttached);
    }

    [Fact]
    public async Task Generated_server_extension_loop_stops_in_the_real_interpreter()
    {
        var events = new EventQueue();
        using var server = DebugServer(KernelDebugPauseScope.Execution);
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(events);
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            RpcKernelLoopControlGenerationTests.SumPositivesContinueSource,
            "Sample.LoopContinuePluginPackage");
        var nodeId = SandboxNodeMap.Create(package.Module).GetDescriptor(Loop(package.Module.Functions[0].Body)).Id;
        _ = await SuccessAsync(debug, PluginDebugCommands.SetBreakpoints, new
        {
            pluginId = package.Manifest.PluginId,
            nodeIds = new[] { nodeId.Value }
        });
        _ = await SuccessAsync(debug, PluginDebugCommands.Attach);
        var kernel = await owner.InstallServerExtensionAsync(package);
        var values = SandboxValue.FromList(
            [SandboxValue.FromInt32(2), SandboxValue.FromInt32(4)],
            SandboxType.I32);

        var run = kernel.InvokeServerExtensionAsync([values]).AsTask();
        var stopped = await events.NextAsync();
        Assert.Equal(nodeId.Value, stopped.GetProperty("nodeId").GetString());
        _ = await SuccessAsync(debug, PluginDebugCommands.SetBreakpoints, new
        {
            pluginId = package.Manifest.PluginId,
            nodeIds = Array.Empty<string>()
        });
        _ = await SuccessAsync(debug, PluginDebugCommands.Continue, new
        {
            runId = stopped.GetProperty("runId").GetString()
        });

        Assert.Equal(6, Assert.IsType<I32Value>(await run).Value);
    }

    private static PluginServer DebugServer(KernelDebugPauseScope scope)
        => PluginServer.Create(
            defaultPolicy: PluginAddendumTestPolicies.LongWall(),
            executionMode: ExecutionMode.Interpreted,
            remoteDebugOptions: new PluginRemoteDebugOptions
            {
                Enabled = true,
                DefaultPauseScope = scope,
                AllowedPauseScopes = [scope]
            });

    private static Task<bool> ExecuteAsync(InstalledKernel kernel, string target)
        => kernel.ShouldHandleAsync(
                DamageEventAdapter.Instance,
                new DamageEvent("fire", 120, target))
            .AsTask();

    private static SandboxNodeId StatementNode(PluginPackage package)
        => SandboxNodeMap.Create(package.Module).Nodes.First(node =>
            node.FunctionId == package.Entrypoints.ShouldHandle && node.Kind == SandboxNodeKind.Statement).Id;

    private static ForRangeStatement Loop(IEnumerable<Statement> statements)
        => Loops(statements).Single();

    private static IEnumerable<ForRangeStatement> Loops(IEnumerable<Statement> statements)
        => statements.SelectMany(statement => statement switch
        {
            ForRangeStatement loop => new[] { loop }.Concat(Loops(loop.Body)),
            IfStatement branch => Loops(branch.Then).Concat(Loops(branch.Else)),
            WhileStatement loop => Loops(loop.Body),
            _ => []
        });

    private static PluginPackage WithPluginId(PluginPackage package, string pluginId)
    {
        var metadata = package.Module.Metadata.ToDictionary();
        metadata["pluginId"] = pluginId;
        return PluginPackage.Create(
            package.Manifest with { PluginId = pluginId },
            package.Module with { Id = pluginId, Metadata = metadata },
            package.Entrypoints);
    }

    private static async Task<JsonElement> SuccessAsync(
        PluginDebugSession session,
        string command,
        object? payload = null)
    {
        var request = new PluginDebugEnvelope(
            PluginDebugProtocol.Version,
            command,
            Guid.NewGuid().ToString("N"),
            session.SessionToken,
            JsonSerializer.SerializeToElement(payload ?? new { }));
        var bytes = await session.ExchangeAsync(PluginDebugProtocol.Encode(request, 1024 * 1024));
        var response = PluginDebugProtocol.Decode(bytes, 1024 * 1024).Payload;
        Assert.True(response.GetProperty("success").GetBoolean(), response.ToString());
        return response.GetProperty("body");
    }

    private sealed class EventQueue : IPluginDebugEventEndpoint
    {
        private readonly Channel<byte[]> _events = Channel.CreateUnbounded<byte[]>();
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public ValueTask PublishAsync(byte[] message, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return _events.Writer.WriteAsync(message, cancellationToken);
        }

        public async Task<JsonElement> NextAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var message = await _events.Reader.ReadAsync(timeout.Token);
            return PluginDebugProtocol.Decode(message, 1024 * 1024).Payload;
        }
    }
}
