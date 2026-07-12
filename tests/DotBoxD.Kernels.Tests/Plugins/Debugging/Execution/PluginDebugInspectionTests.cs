using System.Text.Json;
using System.Threading.Channels;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Execution;

public sealed class PluginDebugInspectionTests
{
    [Fact]
    public async Task Stopped_threads_frames_and_structured_variables_are_inspectable_and_mutable()
    {
        var events = new EventQueue();
        using var server = DebugServer();
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(events);
        var package = FireDamagePluginPackage.Create();
        var nodeId = StatementNode(package);
        _ = await SuccessAsync(
            debug,
            PluginDebugCommands.SetBreakpoints,
            new { pluginId = package.Manifest.PluginId, nodeIds = new[] { nodeId.Value } });
        _ = await SuccessAsync(debug, PluginDebugCommands.Attach);
        var kernel = await owner.InstallAsync(package);
        var execution = kernel.ShouldHandleAsync(
                DamageEventAdapter.Instance,
                new DamageEvent("fire", 120, "player-1"))
            .AsTask();
        var stopped = await events.NextAsync();
        var runId = stopped.GetProperty("runId").GetString()!;

        var threads = await SuccessAsync(debug, PluginDebugCommands.Threads);
        Assert.Equal(runId, Assert.Single(threads.GetProperty("threads").EnumerateArray()).GetProperty("runId").GetString());
        var trace = await SuccessAsync(debug, PluginDebugCommands.StackTrace, new { runId });
        var frame = Assert.Single(trace.GetProperty("frames").EnumerateArray());
        Assert.Equal(package.Entrypoints.ShouldHandle, frame.GetProperty("functionId").GetString());
        var frameId = frame.GetProperty("frameId").GetString()!;

        var variables = await SuccessAsync(debug, PluginDebugCommands.Variables, new { frameId });
        var eventArgument = variables.GetProperty("arguments").EnumerateArray()
            .Single(variable => variable.GetProperty("name").GetString() == "e");
        var amount = eventArgument.GetProperty("value").GetProperty("children").EnumerateArray()
            .Single(variable => variable.GetProperty("name").GetString() == "Amount")
            .GetProperty("value");
        Assert.Equal("I32", amount.GetProperty("type").GetString());
        Assert.Equal(120, amount.GetProperty("value").GetInt32());

        var rejected = await ExchangeAsync(
            debug,
            PluginDebugCommands.SetVariable,
            new { frameId, name = "e_Amount", value = new { type = "I32", value = "wrong" } });
        Assert.False(rejected.GetProperty("success").GetBoolean());
        Assert.Equal("invalidValue", rejected.GetProperty("error").GetProperty("code").GetString());

        var evaluated = await SuccessAsync(
            debug,
            PluginDebugCommands.Evaluate,
            new { frameId, expression = "e.Amount" });
        Assert.Equal(120, evaluated.GetProperty("value").GetProperty("value").GetInt32());

        _ = await SuccessAsync(debug, PluginDebugCommands.SetExpression, new
        {
            frameId,
            expression = "e.Amount",
            valueExpression = "50"
        });
        _ = await SuccessAsync(debug, PluginDebugCommands.Continue, new { runId });

        Assert.False(await execution);
    }

    [Fact]
    public async Task Step_in_and_step_over_stop_the_same_execution_at_later_checkpoints()
    {
        var events = new EventQueue();
        using var server = DebugServer();
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(events);
        var package = FireDamagePluginPackage.Create();
        var nodeId = StatementNode(package);
        _ = await SuccessAsync(
            debug,
            PluginDebugCommands.SetBreakpoints,
            new { pluginId = package.Manifest.PluginId, nodeIds = new[] { nodeId.Value } });
        _ = await SuccessAsync(debug, PluginDebugCommands.Attach);
        var kernel = await owner.InstallAsync(package);
        var execution = kernel.ShouldHandleAsync(
                DamageEventAdapter.Instance,
                new DamageEvent("fire", 120, "player-1"))
            .AsTask();
        var first = await events.NextAsync();
        var runId = first.GetProperty("runId").GetString()!;
        _ = await SuccessAsync(
            debug,
            PluginDebugCommands.SetBreakpoints,
            new { pluginId = package.Manifest.PluginId, nodeIds = Array.Empty<string>() });

        _ = await SuccessAsync(debug, PluginDebugCommands.StepIn, new { runId });
        var second = await events.NextAsync();
        Assert.Equal("step", second.GetProperty("reason").GetString());
        Assert.Equal(runId, second.GetProperty("runId").GetString());

        _ = await SuccessAsync(debug, PluginDebugCommands.StepOver, new { runId });
        var third = await events.NextAsync();
        Assert.Equal("step", third.GetProperty("reason").GetString());
        Assert.Equal(runId, third.GetProperty("runId").GetString());

        _ = await SuccessAsync(debug, PluginDebugCommands.Continue, new { runId });
        Assert.True(await execution);
    }

    [Fact]
    public async Task Logical_call_stack_and_step_out_follow_sandbox_function_depth()
    {
        var events = new EventQueue();
        using var server = DebugServer();
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(events);
        var package = WithDebugHelper(FireDamagePluginPackage.Create());
        var helperNode = SandboxNodeMap.Create(package.Module).Nodes.Single(node =>
            node.FunctionId == "DebugHelper" && node.Kind == SandboxNodeKind.Function).Id;
        _ = await SuccessAsync(
            debug,
            PluginDebugCommands.SetBreakpoints,
            new { pluginId = package.Manifest.PluginId, nodeIds = new[] { helperNode.Value } });
        _ = await SuccessAsync(debug, PluginDebugCommands.Attach);
        var kernel = await owner.InstallAsync(package);
        var execution = kernel.ShouldHandleAsync(
                DamageEventAdapter.Instance,
                new DamageEvent("fire", 120, "player-1"))
            .AsTask();
        var stopped = await events.NextAsync();
        var runId = stopped.GetProperty("runId").GetString()!;
        var trace = await SuccessAsync(debug, PluginDebugCommands.StackTrace, new { runId });
        var frames = trace.GetProperty("frames").EnumerateArray().ToArray();
        Assert.Equal(2, frames.Length);
        Assert.Equal("DebugHelper", frames[0].GetProperty("functionId").GetString());
        Assert.Equal(package.Entrypoints.ShouldHandle, frames[1].GetProperty("functionId").GetString());
        _ = await SuccessAsync(
            debug,
            PluginDebugCommands.SetBreakpoints,
            new { pluginId = package.Manifest.PluginId, nodeIds = Array.Empty<string>() });

        _ = await SuccessAsync(debug, PluginDebugCommands.StepOut, new { runId });
        var callerStop = await events.NextAsync();
        Assert.Equal("step", callerStop.GetProperty("reason").GetString());
        trace = await SuccessAsync(debug, PluginDebugCommands.StackTrace, new { runId });
        Assert.Single(trace.GetProperty("frames").EnumerateArray());

        _ = await SuccessAsync(debug, PluginDebugCommands.Continue, new { runId });
        Assert.True(await execution);
    }

    [Fact]
    public async Task Lease_expiry_cancels_a_blocked_reverse_event_and_resumes_execution()
    {
        var events = new BlockingEvents();
        using var server = PluginServer.Create(
            defaultPolicy: PluginAddendumTestPolicies.LongWall(),
            executionMode: ExecutionMode.Interpreted,
            remoteDebugOptions: new PluginRemoteDebugOptions
            {
                Enabled = true,
                DefaultPauseScope = KernelDebugPauseScope.Execution,
                AllowedPauseScopes = [KernelDebugPauseScope.Execution],
                StopLease = TimeSpan.FromMilliseconds(40)
            });
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(events);
        var package = FireDamagePluginPackage.Create();
        var nodeId = StatementNode(package);
        _ = await SuccessAsync(
            debug,
            PluginDebugCommands.SetBreakpoints,
            new { pluginId = package.Manifest.PluginId, nodeIds = new[] { nodeId.Value } });
        _ = await SuccessAsync(debug, PluginDebugCommands.Attach);
        var kernel = await owner.InstallAsync(package);

        var execution = kernel.ShouldHandleAsync(
                DamageEventAdapter.Instance,
                new DamageEvent("fire", 120, "player-1"))
            .AsTask();
        await events.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(await execution.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(debug.IsAttached);
    }

    private static PluginServer DebugServer()
        => PluginServer.Create(
            defaultPolicy: PluginAddendumTestPolicies.LongWall(),
            executionMode: ExecutionMode.Interpreted,
            remoteDebugOptions: new PluginRemoteDebugOptions
            {
                Enabled = true,
                DefaultPauseScope = KernelDebugPauseScope.Execution,
                AllowedPauseScopes = [KernelDebugPauseScope.Execution]
            });

    private static SandboxNodeId StatementNode(PluginPackage package)
        => SandboxNodeMap.Create(package.Module).Nodes.First(node =>
            node.FunctionId == package.Entrypoints.ShouldHandle && node.Kind == SandboxNodeKind.Statement).Id;

    private static PluginPackage WithDebugHelper(PluginPackage package)
    {
        var shouldHandle = package.Module.Functions.Single(function =>
            function.Id == package.Entrypoints.ShouldHandle);
        var span = shouldHandle.Body[0].Span;
        var helper = new SandboxFunction(
            "DebugHelper",
            false,
            [new Parameter("amount", SandboxType.I32), new Parameter("minimum", SandboxType.I32)],
            SandboxType.Bool,
            [new ReturnStatement(
                new BinaryExpression(
                    new VariableExpression("amount", span),
                    ">=",
                    new VariableExpression("minimum", span),
                    span),
                span)]);
        var rewritten = shouldHandle with
        {
            Body = [new ReturnStatement(
                new CallExpression(
                    helper.Id,
                    [new VariableExpression("e_Amount", span), new VariableExpression("MinDamage", span)],
                    null,
                    span),
                span)]
        };
        var functions = package.Module.Functions
            .Select(function => function.Id == shouldHandle.Id ? rewritten : function)
            .Append(helper)
            .ToArray();
        return PluginPackage.Create(package.Manifest, package.Module with { Functions = functions }, package.Entrypoints);
    }

    private static async Task<JsonElement> SuccessAsync(
        PluginDebugSession session,
        string command,
        object? payload = null)
    {
        var response = await ExchangeAsync(session, command, payload);
        Assert.True(response.GetProperty("success").GetBoolean(), response.ToString());
        return response.GetProperty("body");
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
        return PluginDebugProtocol.Decode(response, 1024 * 1024).Payload;
    }

    private sealed class EventQueue : IPluginDebugEventEndpoint
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

    private sealed class BlockingEvents : IPluginDebugEventEndpoint
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask PublishAsync(byte[] message, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
