using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.LocalCalls;

public sealed class InterpreterLocalFunctionScalarCallSemanticsTests
{
    [Theory]
    [InlineData(0, 7L, 6L)]
    [InlineData(1, 11L, 7L)]
    [InlineData(2, 13L, 10L)]
    [InlineData(3, 9L, 13L)]
    public async Task Arity_zero_through_three_preserve_values_and_resources(
        int arity,
        long expectedValue,
        long expectedFuel)
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, LocalFunctionScalarCallModules.Values(arity));

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, Options());

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expectedValue, ((I64Value)result.Value!).Value);
        Assert.Equal(expectedFuel, result.ResourceUsage.FuelUsed);
        Assert.Equal(0, result.ResourceUsage.LoopIterations);
        Assert.Equal(0, result.ResourceUsage.AllocatedBytes);
        Assert.Equal(0, result.ResourceUsage.HostCalls);
    }

    [Fact]
    public async Task Concurrent_scalar_calls_on_one_plan_keep_each_argument_pair_isolated()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, LocalFunctionScalarCallModules.ConcurrentPair);
        var interpreter = new SandboxInterpreter();
        var options = Options();
        var inputs = Enumerable.Range(0, 32)
            .Select(value => I64List(1_000 + value, 10_000 - value))
            .ToArray();
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = inputs
            .Select(input => Task.Run(async () =>
            {
                await start.Task;
                return await interpreter.ExecuteAsync(
                    plan,
                    "main",
                    input,
                    options,
                    CancellationToken.None);
            }))
            .ToArray();

        start.SetResult(true);
        var results = await Task.WhenAll(tasks);

        for (var i = 0; i < results.Length; i++)
        {
            Assert.True(results[i].Succeeded, results[i].Error?.SafeMessage);
            Assert.Equal((1_000L + i) * 100_000 + 10_000 - i, ((I64Value)results[i].Value!).Value);
        }
    }

    [Fact]
    public async Task Collection_intrinsic_name_keeps_precedence_over_same_named_function()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, LocalFunctionScalarCallModules.CollectionNamePrecedence);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, Options());

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        var list = Assert.IsType<ListValue>(result.Value);
        Assert.Equal(SandboxType.I32, list.ItemType);
        Assert.Equal(7, ((I32Value)Assert.Single(list.Values)).Value);
    }

    private static async Task<ExecutionPlan> PrepareAsync(
        DotBoxD.Hosting.Execution.SandboxHost host,
        string moduleJson)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static SandboxValue I64List(params long[] values)
        => SandboxValue.FromList(values.Select(SandboxValue.FromInt64).ToArray(), SandboxType.I64);

    private static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };
}
