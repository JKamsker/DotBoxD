using System.Text.Json;
using System.Threading.Channels;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Execution;

public sealed class PluginDebugEvaluationTests
{
    [Fact]
    public async Task Sandbox_only_evaluation_reads_frame_values_and_set_expression_uses_validated_write_path()
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
        var initialized = await SuccessAsync(debug, PluginDebugCommands.Initialize);
        Assert.Equal("sandbox-only-v1", initialized.GetProperty("evaluator").GetProperty("id").GetString());
        Assert.Equal("SandboxOnly", initialized.GetProperty("evaluator").GetProperty("trustProfile").GetString());
        var package = FireDamagePluginPackage.Create();
        var node = SandboxNodeMap.Create(package.Module).Nodes.First(candidate =>
            candidate.FunctionId == package.Entrypoints.ShouldHandle && candidate.Kind == SandboxNodeKind.Statement);
        _ = await SuccessAsync(
            debug,
            PluginDebugCommands.SetBreakpoints,
            new { pluginId = package.Manifest.PluginId, nodeIds = new[] { node.Id.Value } });
        _ = await SuccessAsync(debug, PluginDebugCommands.Attach);
        var kernel = await owner.InstallAsync(package);
        var execution = kernel.ShouldHandleAsync(
                DamageEventAdapter.Instance,
                new DamageEvent("fire", 120, "player-1"))
            .AsTask();
        var stopped = await events.NextAsync();
        var runId = stopped.GetProperty("runId").GetString()!;
        var trace = await SuccessAsync(debug, PluginDebugCommands.StackTrace, new { runId });
        var frameId = Assert.Single(trace.GetProperty("frames").EnumerateArray())
            .GetProperty("frameId").GetString()!;

        var evaluated = await SuccessAsync(
            debug,
            PluginDebugCommands.Evaluate,
            new { frameId, expression = "e_Amount >= MinDamage && e_DamageType == \"fire\"" });
        Assert.True(evaluated.GetProperty("value").GetProperty("value").GetBoolean());

        var rejected = await ExchangeAsync(
            debug,
            PluginDebugCommands.Evaluate,
            new { frameId, expression = "System.Environment" });
        Assert.False(rejected.GetProperty("success").GetBoolean());
        Assert.Equal("evaluationFailed", rejected.GetProperty("error").GetProperty("code").GetString());

        rejected = await ExchangeAsync(
            debug,
            PluginDebugCommands.Evaluate,
            new { frameId, expression = "e_Amount", allowAwait = true });
        Assert.False(rejected.GetProperty("success").GetBoolean());
        Assert.Contains("does not support await", rejected.GetProperty("error").GetProperty("message").GetString());

        var changed = await SuccessAsync(
            debug,
            PluginDebugCommands.SetExpression,
            new { frameId, expression = "e_Amount", valueExpression = "MinDamage - 1" });
        Assert.Equal(99, changed.GetProperty("value").GetProperty("value").GetInt32());
        _ = await SuccessAsync(debug, PluginDebugCommands.Continue, new { runId });

        Assert.False(await execution);
    }

    [Fact]
    public void Evaluation_result_requires_exactly_one_value_or_error()
    {
        Assert.Throws<ArgumentNullException>(() => PluginDebugEvaluationResult.Success(null!));
        Assert.Throws<ArgumentNullException>(() => PluginDebugEvaluationResult.Failure(null!));
    }

    [Fact]
    public async Task Sandbox_only_evaluation_promotes_integer_literals_for_i64_expressions()
    {
        var frame = new ReadOnlyFrame("amount", SandboxValue.FromInt64(42));

        var result = await SandboxOnlyPluginDebugEvaluator.Instance.EvaluateAsync(
            new PluginDebugEvaluationRequest(frame, "amount + 1 > 42"));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(SandboxValue.FromBool(true), result.Value);
    }

    [Theory]
    [InlineData("true || missing", true)]
    [InlineData("false && missing", false)]
    public async Task Sandbox_only_evaluation_short_circuits_logical_operators(
        string expression,
        bool expected)
    {
        var frame = new ReadOnlyFrame("amount", SandboxValue.FromInt32(42));

        var result = await SandboxOnlyPluginDebugEvaluator.Instance.EvaluateAsync(
            new PluginDebugEvaluationRequest(frame, expression));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(SandboxValue.FromBool(expected), result.Value);
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

    private sealed class ReadOnlyFrame(string name, SandboxValue value) : ISandboxDebugFrame
    {
        public string FunctionId => "test";

        public int Depth => 0;

        public ISandboxDebugFrame? Caller => null;

        public IReadOnlyList<SandboxDebugVariable> Arguments { get; } =
        [
            new SandboxDebugVariable(
                name,
                value.Type,
                SandboxDebugVariableKind.Argument,
                isAssigned: true,
                value)
        ];

        public IReadOnlyList<SandboxDebugVariable> Locals => [];

        public bool TrySetVariable(string variableName, SandboxValue replacement, out SandboxError? error)
        {
            error = new SandboxError(SandboxErrorCode.InvalidInput, "read only");
            return false;
        }

        public bool TrySetMember(
            string variableName,
            IReadOnlyList<SandboxDebugValuePathSegment> path,
            SandboxValue replacement,
            out SandboxError? error)
        {
            error = new SandboxError(SandboxErrorCode.InvalidInput, "read only");
            return false;
        }
    }
}
