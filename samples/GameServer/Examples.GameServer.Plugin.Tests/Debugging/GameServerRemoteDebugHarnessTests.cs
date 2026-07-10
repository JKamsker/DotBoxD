using System.Text.Json;
using System.Threading.Channels;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Game.Plugin.Kernels;
using DotBoxD.Kernels.Game.Server.Abstractions;
using DotBoxD.Kernels.Game.Server.Abstractions.Events;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Debugging;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Policies;

namespace DotBoxD.Kernels.Game.Plugin.Tests.Debugging;

public sealed class GameServerRemoteDebugHarnessTests
{
    [Fact]
    public async Task Real_sample_kernels_stop_and_resume_through_the_remote_debug_session()
    {
        var events = new RecordingEvents();
        using var server = PluginServer.Create(
            configureHost: host => host.AddBindingsFrom<IGameWorldAccess>(new StubWorld()),
            executionMode: ExecutionMode.Compiled,
            remoteDebugOptions: new PluginRemoteDebugOptions
            {
                Enabled = true,
                DefaultPauseScope = KernelDebugPauseScope.Execution,
                AllowedPauseScopes = [KernelDebugPauseScope.Execution]
            });
        using var session = server.CreateSession();
        await using var debug = session.CreateDebugSession(events);
        _ = await ExchangeAsync(debug, PluginDebugCommands.Attach, new { pauseScope = "execution" });

        var guardian = KernelPackageRegistry.Resolve<GuardianKernel>();
        await AssertStopsAndResumesAsync(
            server,
            session,
            debug,
            events,
            guardian,
            guardian.Entrypoints.ShouldHandle,
            static (kernel, host) => kernel.ShouldHandleAsync(
                host.Events.Resolve<MonsterAggroEvent>(),
                new MonsterAggroEvent("monster-4", "player-1", 2, 8, 2)).AsTask());

        var retaliation = KernelPackageRegistry.Resolve<RetaliationKernel>();
        await AssertStopsAndResumesAsync(
            server,
            session,
            debug,
            events,
            retaliation,
            retaliation.Entrypoints.Handle,
            static (kernel, host) => kernel.HandleAsync(
                host.Events.Resolve<AttackEvent>(),
                new AttackEvent("monster-4", "player-1", 12, 8)).AsTask());

        var blink = KernelPackageRegistry.Resolve<BlinkKernel>();
        await AssertStopsAndResumesAsync(
            server,
            session,
            debug,
            events,
            blink,
            blink.Entrypoints.Handle,
            static (kernel, _) => kernel.InvokeServerExtensionAsync(
                [SandboxValue.FromString("monster-4"), SandboxValue.FromString("player-1")]).AsTask(),
            serverExtension: true);
    }

    private static async Task AssertStopsAndResumesAsync(
        PluginServer server,
        PluginSession session,
        PluginDebugSession debug,
        RecordingEvents events,
        PluginPackage package,
        string functionId,
        Func<InstalledKernel, PluginServer, Task> execute,
        bool serverExtension = false)
    {
        var nodeId = SandboxNodeMap.Create(package.Module).Nodes.First(node =>
            node.FunctionId == functionId && node.Kind == SandboxNodeKind.Statement).Id;
        var pending = await ExchangeAsync(
            debug,
            PluginDebugCommands.SetBreakpoints,
            new { pluginId = package.Manifest.PluginId, nodeIds = new[] { nodeId.Value } });
        Assert.False(pending.GetProperty("breakpoints")[0].GetProperty("verified").GetBoolean());

        var policy = GrantRequiredCapabilities(server.GetRequiredCapabilities(package));
        var kernel = serverExtension
            ? await session.InstallServerExtensionAsync(package, policy)
            : await session.InstallAsync(package, policy);
        var execution = execute(kernel, server);
        var stopped = await events.NextAsync();

        Assert.False(execution.IsCompleted);
        Assert.Equal(package.Manifest.PluginId, stopped.GetProperty("pluginId").GetString());
        Assert.Equal(nodeId.Value, stopped.GetProperty("nodeId").GetString());
        _ = await ExchangeAsync(
            debug,
            PluginDebugCommands.Continue,
            new { runId = stopped.GetProperty("runId").GetString() });

        await execution.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ExecutionMode.Interpreted, kernel.LastExecution?.ActualMode);
    }

    private static SandboxPolicy GrantRequiredCapabilities(IReadOnlyList<string> capabilities)
    {
        var builder = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10));
        foreach (var capability in capabilities)
        {
            if (string.Equals(capability, RuntimeCapabilityIds.Async, StringComparison.Ordinal))
            {
                builder.AllowRuntimeAsync();
            }
            else if (!string.Equals(capability, "host.message.write", StringComparison.Ordinal))
            {
                var effect = capability.Contains(".write.", StringComparison.Ordinal)
                    ? SandboxEffect.HostStateWrite
                    : SandboxEffect.HostStateRead;
                builder.Grant(capability, new { }, effect);
            }
        }

        return builder.Build();
    }

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
        private readonly Channel<byte[]> _events = Channel.CreateUnbounded<byte[]>();

        public ValueTask PublishAsync(byte[] message, CancellationToken cancellationToken = default)
            => _events.Writer.WriteAsync(message, cancellationToken);

        public async Task<JsonElement> NextAsync()
        {
            var message = await _events.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            return PluginDebugProtocol.Decode(message, 1024 * 1024).Payload;
        }
    }

    private sealed class StubWorld : IGameWorldAccess
    {
        public IMonsterControl Monsters { get; } = new StubMonsterControl();
        public IEntityControl Entities { get; } = new StubEntityControl();
    }

    private sealed class StubMonsterControl : IMonsterControl
    {
        public IMonster Get(string entityId) => new StubMonster(entityId);
        public ValueTask<bool> IsMonsterAsync(string entityId) => ValueTask.FromResult(true);
    }

    private sealed class StubEntityControl : IEntityControl
    {
        public IEntity Get(string entityId) => new StubEntity(entityId);
    }

    private sealed class StubMonster(string id) : IMonster
    {
        public string Id { get; } = id;
        public ValueTask<MonsterSnapshot> SnapshotAsync()
            => ValueTask.FromResult(new MonsterSnapshot(Id, Id, 80, 8, 5));
        public ValueTask<bool> KillAsync() => ValueTask.FromResult(true);
        public ValueTask<int> GetThreatAsync() => ValueTask.FromResult(8);
        public ValueTask TeleportToAsync(int position) => ValueTask.CompletedTask;
        public ValueTask<int> GetHealthAsync() => ValueTask.FromResult(80);
        public ValueTask<int> GetLevelAsync() => ValueTask.FromResult(8);
        public ValueTask<int> GetPositionAsync() => ValueTask.FromResult(5);
    }

    private sealed class StubEntity(string id) : IEntity
    {
        public string Id { get; } = id;
        public ValueTask<int> GetHealthAsync() => ValueTask.FromResult(30);
        public ValueTask<int> GetLevelAsync() => ValueTask.FromResult(1);
        public ValueTask<int> GetPositionAsync() => ValueTask.FromResult(5);
    }
}
