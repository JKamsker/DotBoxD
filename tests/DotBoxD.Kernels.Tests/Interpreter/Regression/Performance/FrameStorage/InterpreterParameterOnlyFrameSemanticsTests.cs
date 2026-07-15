using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class InterpreterParameterOnlyFrameSemanticsTests
{
    [Theory]
    [InlineData("I32")]
    [InlineData("I64")]
    [InlineData("F64")]
    public async Task Raw_parameter_can_be_reassigned_and_returned(string type)
    {
        var scenario = Scenario.For(type);
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            ParameterOnlyFrameModules.Reassignment(
                $"parameter-only-reassignment-{type.ToLowerInvariant()}",
                type,
                scenario.IncrementJson));

        var result = await host.ExecuteAsync(plan, "main", scenario.Input, Options());

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        scenario.AssertValue(result.Value!);
    }

    [Fact]
    public async Task Unexecuted_genuine_raw_local_read_remains_a_validation_error()
    {
        using var host = SandboxTestHost.Create();
        var assignedModule = await host.ImportJsonAsync(ParameterOnlyFrameModules.AssignedRawLocal);
        var prepared = await host.PrepareAsync(assignedModule, Policy());
        var unassignedModule = await host.ImportJsonAsync(ParameterOnlyFrameModules.UnassignedRawLocal);
        var tampered = ReplaceModule(prepared, unassignedModule);
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

    [Fact]
    public async Task Parameter_only_helper_frame_reassigns_and_returns_its_argument()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, ParameterOnlyFrameModules.ParameterOnlyHelper);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.FromInt64(40), Options());

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(42, ((I64Value)result.Value!).Value);
    }

    [Fact]
    public async Task Concurrent_executions_on_one_plan_preserve_each_frames_value()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            ParameterOnlyFrameModules.Reassignment(
                "parameter-only-concurrent-frames",
                "I64",
                """{ "i64": 1 }"""));
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
                    SandboxValue.FromInt64(10_000 + value),
                    options,
                    CancellationToken.None);
            }))
            .ToArray();

        start.SetResult(true);
        var results = await Task.WhenAll(tasks);

        for (var i = 0; i < results.Length; i++)
        {
            Assert.True(results[i].Succeeded, results[i].Error?.SafeMessage);
            Assert.Equal(10_001 + i, ((I64Value)results[i].Value!).Value);
        }
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
        => SandboxPolicyBuilder.Create().WithFuel(1_000).Build();

    private static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

    private sealed record Scenario(SandboxValue Input, string IncrementJson, Action<SandboxValue> AssertValue)
    {
        public static Scenario For(string type)
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
