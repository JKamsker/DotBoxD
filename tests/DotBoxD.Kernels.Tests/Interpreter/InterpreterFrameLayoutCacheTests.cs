using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Tests.Interpreter;

public sealed class InterpreterFrameLayoutCacheTests
{
    [Fact]
    public async Task Concurrent_first_executions_share_a_plan_without_corrupting_layouts()
    {
        using var host = CreateHost();
        var module = await host.ImportJsonAsync(MixedScalarModuleJson());
        var plan = await host.PrepareAsync(module, Policy());
        var interpreter = new SandboxInterpreter();
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 32)
            .Select(value => Task.Run(async () =>
            {
                await start.Task;
                return await interpreter.ExecuteAsync(
                    plan,
                    "main",
                    SandboxValue.FromInt32(value),
                    Options(),
                    CancellationToken.None);
            }))
            .ToArray();

        start.SetResult(true);
        var results = await Task.WhenAll(tasks);

        for (var i = 0; i < results.Length; i++)
        {
            Assert.True(results[i].Succeeded, results[i].Error?.SafeMessage);
            Assert.Equal(i + 1, ((I32Value)results[i].Value!).Value);
        }
    }

    [Fact]
    public async Task Same_function_id_in_different_plans_uses_each_plans_layout()
    {
        using var host = CreateHost();
        var intModule = await host.ImportJsonAsync(Int32ModuleJson());
        var floatModule = await host.ImportJsonAsync(Float64ModuleJson());
        var policy = Policy();
        var intPlan = await host.PrepareAsync(intModule, policy);
        var floatPlan = await host.PrepareAsync(floatModule, policy);
        var interpreter = new SandboxInterpreter();

        for (var i = 0; i < 4; i++)
        {
            var intResult = await interpreter.ExecuteAsync(
                intPlan,
                "main",
                SandboxValue.FromInt32(40 + i),
                Options(),
                CancellationToken.None);
            var floatResult = await interpreter.ExecuteAsync(
                floatPlan,
                "main",
                SandboxValue.FromDouble(1.25 + i),
                Options(),
                CancellationToken.None);

            Assert.True(intResult.Succeeded, intResult.Error?.SafeMessage);
            Assert.Equal(42 + i, ((I32Value)intResult.Value!).Value);
            Assert.True(floatResult.Succeeded, floatResult.Error?.SafeMessage);
            Assert.Equal(1.75 + i, ((F64Value)floatResult.Value!).Value);
        }
    }

    private static SandboxHost CreateHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });

    private static SandboxPolicy Policy()
        => SandboxPolicyBuilder.Create()
            .AllowPureComputation()
            .WithFuel(1_000_000)
            .WithMaxAllocatedBytes(1_000_000)
            .Build();

    private static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

    private static string MixedScalarModuleJson()
        => """
        {
          "id": "frame-layout-concurrent",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "value", "type": "I32" }],
            "returnType": "I32",
            "body": [
              { "op": "set", "name": "wide", "value": { "call": "numeric.toI64", "args": [{ "var": "value" }] } },
              { "op": "set", "name": "fraction", "value": { "call": "numeric.toF64", "args": [{ "var": "value" }] } },
              { "op": "set", "name": "result", "value": { "op": "add", "left": { "var": "value" }, "right": { "i32": 1 } } },
              { "op": "return", "value": { "var": "result" } }
            ]
          }]
        }
        """;

    private static string Int32ModuleJson()
        => """
        {
          "id": "frame-layout-int",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "value", "type": "I32" }],
            "returnType": "I32",
            "body": [
              { "op": "set", "name": "shared", "value": { "op": "add", "left": { "var": "value" }, "right": { "i32": 2 } } },
              { "op": "return", "value": { "var": "shared" } }
            ]
          }]
        }
        """;

    private static string Float64ModuleJson()
        => """
        {
          "id": "frame-layout-float",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "value", "type": "F64" }],
            "returnType": "F64",
            "body": [
              { "op": "set", "name": "shared", "value": { "op": "add", "left": { "var": "value" }, "right": { "f64": 0.5 } } },
              { "op": "return", "value": { "var": "shared" } }
            ]
          }]
        }
        """;
}
