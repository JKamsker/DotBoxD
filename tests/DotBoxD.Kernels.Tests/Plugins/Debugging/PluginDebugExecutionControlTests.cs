using System.Text.Json;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging;

public sealed class PluginDebugExecutionControlTests
{
    [Fact]
    public async Task Pending_node_breakpoint_pauses_real_interpreter_and_continue_resumes_it()
    {
        var events = new RecordingEvents();
        using var server = DebugServer(KernelDebugPauseScope.Execution, ExecutionMode.Compiled);
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(events);
        var package = FireDamagePluginPackage.Create();
        var nodeId = StatementNode(package, package.Entrypoints.ShouldHandle);
        var pending = await ExchangeAsync(
            debug,
            PluginDebugCommands.SetBreakpoints,
            new { pluginId = package.Manifest.PluginId, nodeIds = new[] { nodeId.Value } });
        Assert.False(pending.GetProperty("breakpoints")[0].GetProperty("verified").GetBoolean());
        _ = await ExchangeAsync(
            debug,
            PluginDebugCommands.Attach,
            new { pauseScope = "execution" });
        var kernel = await owner.InstallAsync(package);
        var verified = await ExchangeAsync(
            debug,
            PluginDebugCommands.SetBreakpoints,
            new { pluginId = package.Manifest.PluginId, nodeIds = new[] { nodeId.Value } });
        Assert.True(verified.GetProperty("breakpoints")[0].GetProperty("verified").GetBoolean());

        var execution = kernel.ShouldHandleAsync(
                DamageEventAdapter.Instance,
                new DamageEvent("fire", 120, "player-1"))
            .AsTask();
        var stopped = await events.NextAsync();

        Assert.False(execution.IsCompleted);
        Assert.Equal("breakpoint", stopped.GetProperty("reason").GetString());
        Assert.Equal(nodeId.Value, stopped.GetProperty("nodeId").GetString());

        _ = await ExchangeAsync(
            debug,
            PluginDebugCommands.Continue,
            new { runId = stopped.GetProperty("runId").GetString() });

        Assert.True(await execution);
        Assert.Equal(ExecutionMode.Interpreted, kernel.LastExecution?.ActualMode);
    }

    [Fact]
    public async Task Server_pause_parks_new_dispatches_without_exposing_foreign_frames()
    {
        var events = new RecordingEvents();
        using var server = DebugServer(KernelDebugPauseScope.Server, ExecutionMode.Interpreted);
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(events);
        var package = FireDamagePluginPackage.Create();
        var nodeId = StatementNode(package, package.Entrypoints.ShouldHandle);
        _ = await ExchangeAsync(
            debug,
            PluginDebugCommands.SetBreakpoints,
            new { pluginId = package.Manifest.PluginId, nodeIds = new[] { nodeId.Value } });
        _ = await ExchangeAsync(debug, PluginDebugCommands.Attach);
        var owned = await owner.InstallAsync(package);
        var foreignMetadata = package.Module.Metadata.ToDictionary();
        foreignMetadata["pluginId"] = "foreign-fire-damage";
        var foreignPackage = PluginPackage.Create(
            package.Manifest with { PluginId = "foreign-fire-damage" },
            package.Module with { Id = "foreign-fire-damage", Metadata = foreignMetadata },
            package.Entrypoints);
        var foreign = await server.InstallAsync(foreignPackage);

        var stoppedExecution = owned.ShouldHandleAsync(
                DamageEventAdapter.Instance,
                new DamageEvent("fire", 120, "owner"))
            .AsTask();
        var stopped = await events.NextAsync();
        var foreignExecution = foreign.ShouldHandleAsync(
                DamageEventAdapter.Instance,
                new DamageEvent("fire", 120, "foreign"))
            .AsTask();

        await Task.Delay(30);
        Assert.False(foreignExecution.IsCompleted);
        Assert.False(stoppedExecution.IsCompleted);

        _ = await ExchangeAsync(
            debug,
            PluginDebugCommands.Continue,
            new { runId = stopped.GetProperty("runId").GetString() });

        Assert.True(await stoppedExecution);
        Assert.True(await foreignExecution);
        Assert.Equal(1, events.Count);
    }

    [Fact]
    public async Task Disconnect_while_stopped_releases_the_execution_and_removes_debug_hooks()
    {
        var events = new RecordingEvents();
        using var server = DebugServer(KernelDebugPauseScope.Server, ExecutionMode.Interpreted);
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(events);
        var package = FireDamagePluginPackage.Create();
        var nodeId = StatementNode(package, package.Entrypoints.ShouldHandle);
        _ = await ExchangeAsync(
            debug,
            PluginDebugCommands.SetBreakpoints,
            new { pluginId = package.Manifest.PluginId, nodeIds = new[] { nodeId.Value } });
        _ = await ExchangeAsync(debug, PluginDebugCommands.Attach);
        var kernel = await owner.InstallAsync(package);
        var execution = kernel.ShouldHandleAsync(
                DamageEventAdapter.Instance,
                new DamageEvent("fire", 120, "player-1"))
            .AsTask();
        _ = await events.NextAsync();

        _ = await ExchangeAsync(debug, PluginDebugCommands.Disconnect);

        Assert.True(await execution);
        var next = await kernel.ShouldHandleAsync(
            DamageEventAdapter.Instance,
            new DamageEvent("fire", 120, "player-2"));
        Assert.True(next);
        Assert.Equal(1, events.Count);
    }

    [Fact]
    public async Task Canceling_a_stopped_execution_releases_its_server_dispatch_gate()
    {
        var events = new BlockingEvents();
        using var server = DebugServer(KernelDebugPauseScope.Server, ExecutionMode.Interpreted);
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(events);
        var package = FireDamagePluginPackage.Create();
        var nodeId = StatementNode(package, package.Entrypoints.ShouldHandle);
        _ = await ExchangeAsync(
            debug,
            PluginDebugCommands.SetBreakpoints,
            new { pluginId = package.Manifest.PluginId, nodeIds = new[] { nodeId.Value } });
        _ = await ExchangeAsync(debug, PluginDebugCommands.Attach);
        var kernel = await owner.InstallAsync(package);
        using var cancellation = new CancellationTokenSource();

        var execution = kernel.ShouldHandleAsync(
                DamageEventAdapter.Instance,
                new DamageEvent("fire", 120, "canceled"),
                cancellation.Token)
            .AsTask();
        await events.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        _ = await ExchangeAsync(
            debug,
            PluginDebugCommands.SetBreakpoints,
            new { pluginId = package.Manifest.PluginId, nodeIds = Array.Empty<string>() });

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        var next = kernel.ShouldHandleAsync(
                DamageEventAdapter.Instance,
                new DamageEvent("fire", 120, "next"))
            .AsTask();
        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Pause_request_stops_the_next_safe_interpreter_checkpoint()
    {
        var events = new RecordingEvents();
        using var server = DebugServer(KernelDebugPauseScope.Execution, ExecutionMode.Interpreted);
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(events);
        var package = FireDamagePluginPackage.Create();
        _ = await ExchangeAsync(debug, PluginDebugCommands.Attach);
        var kernel = await owner.InstallAsync(package);

        _ = await ExchangeAsync(debug, PluginDebugCommands.Pause);
        var execution = kernel.ShouldHandleAsync(
                DamageEventAdapter.Instance,
                new DamageEvent("fire", 120, "paused"))
            .AsTask();
        var stopped = await events.NextAsync();

        Assert.False(execution.IsCompleted);
        Assert.Equal("pause", stopped.GetProperty("reason").GetString());
        _ = await ExchangeAsync(
            debug,
            PluginDebugCommands.Continue,
            new { runId = stopped.GetProperty("runId").GetString() });
        Assert.True(await execution);
    }

    private static PluginServer DebugServer(KernelDebugPauseScope scope, ExecutionMode executionMode)
        => PluginServer.Create(
            defaultPolicy: PluginAddendumTestPolicies.LongWall(),
            executionMode: executionMode,
            remoteDebugOptions: new PluginRemoteDebugOptions
            {
                Enabled = true,
                DefaultPauseScope = scope,
                AllowedPauseScopes = [scope]
            });

    private static SandboxNodeId StatementNode(PluginPackage package, string functionId)
        => SandboxNodeMap.Create(package.Module).Nodes.First(node =>
            node.FunctionId == functionId && node.Kind == SandboxNodeKind.Statement).Id;

    private static async Task<JsonElement> ExchangeAsync(
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
        var response = await session.ExchangeAsync(PluginDebugProtocol.Encode(request, 1024 * 1024));
        var envelope = PluginDebugProtocol.Decode(response, 1024 * 1024);
        Assert.True(envelope.Payload.GetProperty("success").GetBoolean());
        return envelope.Payload.GetProperty("body");
    }

    private sealed class RecordingEvents : IPluginDebugEventEndpoint
    {
        private readonly TaskCompletionSource<byte[]> _next =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public ValueTask PublishAsync(byte[] message, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            _next.TrySetResult(message);
            return ValueTask.CompletedTask;
        }

        public async Task<JsonElement> NextAsync()
        {
            var message = await _next.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return PluginDebugProtocol.Decode(message, 1024 * 1024).Payload;
        }
    }

    private sealed class BlockingEvents : IPluginDebugEventEndpoint
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask PublishAsync(byte[] message, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
