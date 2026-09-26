using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Replay;

namespace DotBoxD.Kernels.Tests.Plugins.Replay;

public sealed class ExecutionReplayTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Captured_real_binding_execution_roundtrips_and_replays_offline(ExecutionMode mode)
    {
        var invoked = 0;
        var binding = Binding((_, args, _) =>
        {
            invoked++;
            return ValueTask.FromResult(SandboxValue.FromInt32(((I32Value)args[0]).Value + 1));
        });
        var trace = await ExecutionRecording.CaptureAsync(Module(), Policy(), [binding], "main", SandboxValue.FromInt32(41));
        Assert.Equal(1, invoked);
        Assert.Single(trace.Calls);
        var restored = ExecutionTrace.Deserialize(trace.Serialize());
        var replayed = await ExecutionReplay.RunAsync(restored, mode);
        Assert.True(replayed.Matches, replayed.Difference);
        Assert.Equal(SandboxValue.FromInt32(42), replayed.Execution.Value);
        Assert.Equal(mode, replayed.Execution.ActualMode);
        Assert.Equal(1, invoked);
    }

    [Fact]
    public async Task Binding_failure_and_pre_dispatch_cancellation_replay()
    {
        var binding = Binding((_, _, _) => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.NotFound, "secret-path")));
        var trace = await ExecutionRecording.CaptureAsync(Module(), Policy(), [binding], "main", SandboxValue.FromInt32(1));
        Assert.Contains("secret-path", trace.Serialize(), StringComparison.Ordinal);
        var redacted = trace.Redact(value => value with
        {
            Calls = value.Calls.Select(call => call with { Error = call.Error! with { SafeMessage = "redacted" } }).ToArray()
        });
        Assert.DoesNotContain("secret-path", redacted.Serialize(), StringComparison.Ordinal);
        var replayed = await ExecutionReplay.RunAsync(ExecutionTrace.Deserialize(trace.Serialize()));
        Assert.True(replayed.Matches, $"{replayed.Difference} recorded={trace.ErrorCode}, actual={replayed.Execution.Error}, calls={trace.Calls.Count}");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cancelled = await ExecutionRecording.CaptureAsync(Module(), Policy(), [binding], "main", SandboxValue.FromInt32(1), cancellationToken: cancellation.Token);
        Assert.Empty(cancelled.Calls);
        Assert.True((await ExecutionReplay.RunAsync(cancelled)).Matches);
    }

    [Fact]
    public async Task Tampered_calls_redaction_and_identity_are_not_silent_successes()
    {
        var trace = await ExecutionRecording.CaptureAsync(Module(), Policy(), [Binding((_, args, _) => ValueTask.FromResult(args[0]))],
            "main", SandboxValue.FromInt32(1));
        Assert.False((await ExecutionReplay.RunAsync(trace with { Calls = [] })).Matches);
        var changed = trace.Calls[0] with { Arguments = [TraceValue.Capture(SandboxValue.FromInt32(99))] };
        Assert.False((await ExecutionReplay.RunAsync(trace with { Calls = [changed] })).Matches);
        await Assert.ThrowsAsync<InvalidDataException>(() => ExecutionReplay.RunAsync(trace with { ModuleHash = "tampered" }));
        await Assert.ThrowsAsync<InvalidDataException>(() => ExecutionReplay.RunAsync(trace.Redact(value => value with { Calls = [] })));
        Assert.Throws<InvalidDataException>(() => ExecutionTrace.Deserialize((trace with { FormatVersion = 999 }).Serialize()));
    }

    [Fact]
    public void Composite_values_roundtrip_without_loading_clr_types()
    {
        var values = new SandboxValue[]
        {
            SandboxValue.Unit, SandboxValue.FromInt64(long.MaxValue), SandboxValue.FromDouble(1.25),
            SandboxValue.FromGuid(Guid.Empty), SandboxValue.FromPath("cache/data"), SandboxValue.FromUri("https://example.com"),
            SandboxValue.FromOpaqueId("PlayerId", "player-1"),
            SandboxValue.FromList([], SandboxType.String),
            SandboxValue.FromRecord([SandboxValue.FromString("secret"), SandboxValue.FromBool(true)]),
            SandboxValue.FromMap(new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromString("key")] = SandboxValue.FromInt32(2)
            }, SandboxType.String, SandboxType.I32)
        };
        foreach (var value in values)
        {
            Assert.Equal(value, TraceValue.Capture(value).Restore());
        }
    }

    private static SandboxPolicy Policy() => SandboxPolicyBuilder.Create().WithFuel(10_000).Build()
        with
    { ResourceLimits = new ResourceLimits(MaxWallTime: TimeSpan.FromSeconds(5)) };

    private static BindingDescriptor Binding(BindingInvoker invoke) => new("test.lookup", SemVersion.One,
        [SandboxType.I32], SandboxType.I32, SandboxEffect.Cpu, null, BindingCostModel.Fixed(1),
        AuditLevel.None, BindingSafety.PureIntrinsic, invoke,
        CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static SandboxModule Module() => new("replay-test", SemVersion.One, SemVersion.One, [],
        [new SandboxFunction("main", true, [new Parameter("value", SandboxType.I32)], SandboxType.I32,
            [new ReturnStatement(new CallExpression("test.lookup", [new VariableExpression("value", new SourceSpan(1, 1))],
                null, new SourceSpan(1, 1)), new SourceSpan(1, 1))])], new Dictionary<string, string>());
}
