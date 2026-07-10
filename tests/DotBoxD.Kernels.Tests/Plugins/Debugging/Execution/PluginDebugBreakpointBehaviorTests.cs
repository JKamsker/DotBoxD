using System.Text.Json;
using System.Threading.Channels;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Debugging;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Execution;

public sealed class PluginDebugBreakpointBehaviorTests
{
    [Fact]
    public async Task Conditional_breakpoint_uses_sandbox_only_frame_evaluation()
    {
        var fixture = await DebugFixture.CreateAsync();
        await using var cleanup = fixture;
        _ = await fixture.SetBreakpointAsync(new { condition = "e_Amount > 200" });

        var ignored = fixture.ExecuteAsync(120);
        Assert.True(await ignored.WaitAsync(TimeSpan.FromSeconds(2)));

        _ = await fixture.SetBreakpointAsync(new { condition = "e_Amount >= MinDamage" });
        var execution = fixture.ExecuteAsync(120);
        var stopped = await fixture.Events.NextAsync();

        Assert.Equal("breakpoint", stopped.GetProperty("reason").GetString());
        Assert.False(execution.IsCompleted);
        _ = await fixture.SuccessAsync(
            PluginDebugCommands.Continue,
            new { runId = stopped.GetProperty("runId").GetString() });
        Assert.True(await execution);
    }

    [Fact]
    public async Task Hit_count_stops_on_the_requested_occurrence()
    {
        var fixture = await DebugFixture.CreateAsync();
        await using var cleanup = fixture;
        _ = await fixture.SetBreakpointAsync(new { hitCount = 2 });

        Assert.True(await fixture.ExecuteAsync(120).WaitAsync(TimeSpan.FromSeconds(2)));
        var second = fixture.ExecuteAsync(120);
        var stopped = await fixture.Events.NextAsync();

        Assert.Equal("breakpoint", stopped.GetProperty("reason").GetString());
        _ = await fixture.SuccessAsync(
            PluginDebugCommands.Continue,
            new { runId = stopped.GetProperty("runId").GetString() });
        Assert.True(await second);
    }

    [Fact]
    public async Task Logpoint_interpolates_sandbox_expressions_without_stopping()
    {
        var fixture = await DebugFixture.CreateAsync();
        await using var cleanup = fixture;
        _ = await fixture.SetBreakpointAsync(new { logMessage = "damage={e_Amount}, minimum={MinDamage}" });

        var execution = fixture.ExecuteAsync(120);
        var output = await fixture.Events.NextAsync();

        Assert.Equal("console", output.GetProperty("category").GetString());
        Assert.Equal("damage=120, minimum=100", output.GetProperty("output").GetString());
        Assert.True(await execution);
    }

    [Fact]
    public async Task Non_boolean_breakpoint_condition_reports_error_and_stops()
    {
        var fixture = await DebugFixture.CreateAsync();
        await using var cleanup = fixture;
        _ = await fixture.SetBreakpointAsync(new { condition = "e_Amount" });

        var execution = fixture.ExecuteAsync(120);
        var error = await fixture.Events.NextAsync();
        var stopped = await fixture.Events.NextAsync();

        Assert.Equal("stderr", error.GetProperty("category").GetString());
        Assert.Contains("Bool", error.GetProperty("output").GetString(), StringComparison.Ordinal);
        Assert.Equal("breakpointConditionError", stopped.GetProperty("reason").GetString());
        _ = await fixture.SuccessAsync(
            PluginDebugCommands.Continue,
            new { runId = stopped.GetProperty("runId").GetString() });
        Assert.True(await execution);
    }

    [Fact]
    public async Task Logpoint_renders_invalid_failed_and_unclosed_interpolations_without_stopping()
    {
        var fixture = await DebugFixture.CreateAsync();
        await using var cleanup = fixture;
        _ = await fixture.SetBreakpointAsync(new
        {
            logMessage = "bool={e_Amount > 0}, string={e_DamageType}, empty={}, error={missing}, tail={unterminated"
        });

        var execution = fixture.ExecuteAsync(120);
        var output = await fixture.Events.NextAsync();

        Assert.Equal("console", output.GetProperty("category").GetString());
        var text = output.GetProperty("output").GetString();
        Assert.StartsWith("bool=true, string=fire, empty=<invalid expression>, error=<error:", text, StringComparison.Ordinal);
        Assert.EndsWith("tail={unterminated", text, StringComparison.Ordinal);
        Assert.True(await execution);
    }

    [Fact]
    public async Task Sandbox_exception_publishes_a_stop_before_the_runtime_error_escapes()
    {
        var events = new EventQueue();
        using var server = PluginServer.Create(
            defaultPolicy: PluginAddendumTestPolicies.LongWall(),
            executionMode: ExecutionMode.Interpreted,
            remoteDebugOptions: new PluginRemoteDebugOptions
            {
                Enabled = true,
                DefaultPauseScope = KernelDebugPauseScope.Execution,
                AllowedPauseScopes = [KernelDebugPauseScope.Execution]
            });
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(events);
        _ = await SuccessAsync(debug, PluginDebugCommands.Attach);
        var package = FailingPackage();
        var kernel = await owner.InstallAsync(package);

        var execution = kernel.ShouldHandleAsync(
                DamageEventAdapter.Instance,
                new DamageEvent("fire", 120, "player-1"))
            .AsTask();
        var stopped = await events.NextAsync();

        Assert.Equal("exception", stopped.GetProperty("reason").GetString());
        Assert.True(stopped.TryGetProperty("error", out var error));
        Assert.NotEqual(JsonValueKind.Null, error.ValueKind);
        _ = await SuccessAsync(debug, PluginDebugCommands.Continue, new
        {
            runId = stopped.GetProperty("runId").GetString()
        });
        await Assert.ThrowsAsync<SandboxRuntimeException>(() => execution);
    }

    private static PluginPackage FailingPackage()
    {
        var package = FireDamagePluginPackage.Create();
        var function = package.Module.Functions.Single(candidate =>
            candidate.Id == package.Entrypoints.ShouldHandle);
        var span = function.Body[0].Span;
        var division = new BinaryExpression(
            new LiteralExpression(SandboxValue.FromInt32(1), span),
            "/",
            new LiteralExpression(SandboxValue.FromInt32(0), span),
            span);
        var comparison = new BinaryExpression(
            division,
            ">",
            new LiteralExpression(SandboxValue.FromInt32(0), span),
            span);
        var rewritten = function with { Body = [new ReturnStatement(comparison, span)] };
        var functions = package.Module.Functions
            .Select(candidate => candidate.Id == function.Id ? rewritten : candidate)
            .ToArray();
        return PluginPackage.Create(
            package.Manifest,
            package.Module with { Functions = functions },
            package.Entrypoints);
    }

    private sealed class DebugFixture : IAsyncDisposable
    {
        private readonly PluginServer _server;
        private readonly PluginSession _owner;
        private readonly PluginDebugSession _debug;
        private readonly InstalledKernel _kernel;
        private readonly PluginPackage _package;
        private readonly SandboxNodeId _nodeId;

        private DebugFixture(
            PluginServer server,
            PluginSession owner,
            PluginDebugSession debug,
            InstalledKernel kernel,
            PluginPackage package,
            SandboxNodeId nodeId,
            EventQueue events)
        {
            _server = server;
            _owner = owner;
            _debug = debug;
            _kernel = kernel;
            _package = package;
            _nodeId = nodeId;
            Events = events;
        }

        public EventQueue Events { get; }

        public static async Task<DebugFixture> CreateAsync()
        {
            var events = new EventQueue();
            var server = PluginServer.Create(
                defaultPolicy: PluginAddendumTestPolicies.LongWall(),
                executionMode: ExecutionMode.Interpreted,
                remoteDebugOptions: new PluginRemoteDebugOptions
                {
                    Enabled = true,
                    DefaultPauseScope = KernelDebugPauseScope.Execution,
                    AllowedPauseScopes = [KernelDebugPauseScope.Execution]
                });
            var owner = server.CreateSession();
            var debug = owner.CreateDebugSession(events);
            _ = await PluginDebugBreakpointBehaviorTests.SuccessAsync(debug, PluginDebugCommands.Attach);
            var package = FireDamagePluginPackage.Create();
            var kernel = await owner.InstallAsync(package);
            var nodeId = SandboxNodeMap.Create(package.Module).Nodes.First(node =>
                node.FunctionId == package.Entrypoints.ShouldHandle && node.Kind == SandboxNodeKind.Statement).Id;
            return new DebugFixture(server, owner, debug, kernel, package, nodeId, events);
        }

        public Task<bool> ExecuteAsync(int amount)
            => _kernel.ShouldHandleAsync(
                    DamageEventAdapter.Instance,
                    new DamageEvent("fire", amount, "player-1"))
                .AsTask();

        public Task<JsonElement> SetBreakpointAsync(object settings)
        {
            var settingsJson = JsonSerializer.SerializeToElement(settings);
            var breakpoint = new Dictionary<string, object?>
            {
                ["nodeId"] = _nodeId.Value
            };
            foreach (var property in settingsJson.EnumerateObject())
            {
                breakpoint[property.Name] = property.Value.Clone();
            }

            return PluginDebugBreakpointBehaviorTests.SuccessAsync(
                _debug,
                PluginDebugCommands.SetBreakpoints,
                new { pluginId = _package.Manifest.PluginId, breakpoints = new[] { breakpoint } });
        }

        public Task<JsonElement> SuccessAsync(string command, object? payload = null)
            => PluginDebugBreakpointBehaviorTests.SuccessAsync(_debug, command, payload);

        public async ValueTask DisposeAsync()
        {
            await _debug.DisposeAsync();
            _owner.Dispose();
            _server.Dispose();
        }
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
        var responseBytes = await session.ExchangeAsync(PluginDebugProtocol.Encode(request, 1024 * 1024));
        var response = PluginDebugProtocol.Decode(responseBytes, 1024 * 1024).Payload;
        Assert.True(response.GetProperty("success").GetBoolean(), response.ToString());
        return response.GetProperty("body");
    }

    public sealed class EventQueue : IPluginDebugEventEndpoint
    {
        private readonly Channel<byte[]> _events = Channel.CreateUnbounded<byte[]>();

        public ValueTask PublishAsync(byte[] message, CancellationToken cancellationToken = default)
            => _events.Writer.WriteAsync(message, cancellationToken);

        public async Task<JsonElement> NextAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var message = await _events.Reader.ReadAsync(timeout.Token);
            return PluginDebugProtocol.Decode(message, 1024 * 1024).Payload;
        }
    }
}
