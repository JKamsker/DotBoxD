using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class InterpreterMixedFrameSemanticsTests
{
    [Theory]
    [InlineData("I32")]
    [InlineData("I64")]
    [InlineData("F64")]
    public async Task Raw_parameter_can_be_reassigned_when_the_frame_has_a_boxed_local(string type)
    {
        var scenario = ReassignmentScenario.For(type);
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            MixedFrameAssignmentModules.ReassignedRawParameter(
                $"mixed-frame-reassigned-{type.ToLowerInvariant()}-parameter",
                type,
                scenario.IncrementJson));

        var result = await host.ExecuteAsync(plan, "main", scenario.Input, Options());

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        scenario.AssertValue(result.Value!);
        Assert.Equal(9, result.ResourceUsage.FuelUsed);
        Assert.Equal(0, result.ResourceUsage.LoopIterations);
        Assert.Equal(10, result.ResourceUsage.AllocatedBytes);
        Assert.Equal(0, result.ResourceUsage.HostCalls);
    }

    [Fact]
    public async Task Scalar_local_call_preserves_a_mixed_helpers_raw_parameter()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, MixedFrameAssignmentModules.MixedRawParameterHelper);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.FromInt64(40), Options());

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(42, ((I64Value)result.Value!).Value);
        Assert.Equal(13, result.ResourceUsage.FuelUsed);
        Assert.Equal(0, result.ResourceUsage.LoopIterations);
        Assert.Equal(10, result.ResourceUsage.AllocatedBytes);
        Assert.Equal(0, result.ResourceUsage.HostCalls);
    }

    [Fact]
    public async Task Concurrent_mixed_frames_preserve_each_raw_parameter()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, MixedFrameAssignmentModules.RawAndBoxedResult);
        var interpreter = new SandboxInterpreter();
        var options = Options();
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 32)
            .Select(value => Task.Run(async () =>
            {
                await start.Task;
                return await interpreter.ExecuteAsync(
                    plan,
                    "main",
                    SandboxValue.FromInt32(10_000 + value),
                    options,
                    CancellationToken.None);
            }))
            .ToArray();

        start.SetResult(true);
        var results = await Task.WhenAll(tasks);

        for (var i = 0; i < results.Length; i++)
        {
            Assert.True(results[i].Succeeded, results[i].Error?.SafeMessage);
            Assert.Equal(10_005 + i, ((I32Value)results[i].Value!).Value);
        }
    }

    [Fact]
    public async Task Skipped_boxed_assignment_in_a_mixed_frame_remains_a_validation_error()
    {
        using var host = SandboxTestHost.Create();
        var assignedModule = await host.ImportJsonAsync(MixedFrameAssignmentModules.AssignedBoxedLocal);
        var prepared = await host.PrepareAsync(assignedModule, Policy());
        var skippedModule = await host.ImportJsonAsync(MixedFrameAssignmentModules.SkippedBoxedLocal);
        var tampered = ReplaceModule(prepared, skippedModule);
        var interpreter = new SandboxInterpreter();

        var result = await interpreter.ExecuteAsync(
            tampered,
            "main",
            SandboxValue.FromInt32(1),
            Options(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.Equal("local 'label' read before assignment", result.Error.SafeMessage);
    }

    [Fact]
    public async Task Zero_iteration_raw_loop_local_remains_a_validation_error()
    {
        using var host = SandboxTestHost.Create();
        var assignedModule = await host.ImportJsonAsync(MixedFrameAssignmentModules.AssignedRawLoopLocal);
        var prepared = await host.PrepareAsync(assignedModule, Policy());
        var skippedModule = await host.ImportJsonAsync(MixedFrameAssignmentModules.SkippedRawLoopLocal);
        var tampered = ReplaceModule(prepared, skippedModule);
        var interpreter = new SandboxInterpreter();

        var result = await interpreter.ExecuteAsync(
            tampered,
            "main",
            SandboxValue.Unit,
            Options(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.Equal("local 'value' read before assignment", result.Error.SafeMessage);
    }

    private static async Task<ExecutionPlan> PrepareAsync(
        DotBoxD.Hosting.Execution.SandboxHost host,
        string moduleJson)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(module, Policy());
    }

    private static ExecutionPlan ReplaceModule(ExecutionPlan plan, SandboxModule module)
        => new(
            plan.ModuleHash,
            plan.PlanHash,
            plan.PlanSeal,
            plan.PolicyHash,
            plan.BindingManifestHash,
            module,
            plan.Policy,
            plan.Bindings,
            plan.Budget,
            plan.FunctionAnalysis,
            plan.BindingReferences);

    private static SandboxPolicy Policy()
        => SandboxPolicyBuilder.Create().WithFuel(1_000).WithMaxLoopIterations(10).Build();

    private static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

    private sealed record ReassignmentScenario(
        SandboxValue Input,
        string IncrementJson,
        Action<SandboxValue> AssertValue)
    {
        public static ReassignmentScenario For(string type)
            => type switch
            {
                "I32" => new(
                    SandboxValue.FromInt32(40),
                    """{ "i32": 2 }""",
                    value => Assert.Equal(42, ((I32Value)value).Value)),
                "I64" => new(
                    SandboxValue.FromInt64(40),
                    """{ "i64": 2 }""",
                    value => Assert.Equal(42, ((I64Value)value).Value)),
                "F64" => new(
                    SandboxValue.FromDouble(40.5),
                    """{ "f64": 1.5 }""",
                    value => Assert.Equal(42, ((F64Value)value).Value)),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unknown raw parameter type")
            };
    }
}
