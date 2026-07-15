using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class I64SingleAssignmentLoopFallbackTests
{
    [Fact]
    public async Task Unsupported_single_i64_expression_uses_the_general_loop_path()
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(ModuleJson);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithMaxLoopIterations(10)
                .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Interpreted,
                AllowFallbackToInterpreter = false,
                SuppressSuccessfulRunSummaryAudit = true
            });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Equal(5, ((I64Value)result.Value!).Value);
        Assert.Equal(1, result.ResourceUsage.LoopIterations);
    }

    private const string ModuleJson = """
    {
      "id": "i64-single-assignment-loop-fallback",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I64",
        "body": [
          { "op": "set", "name": "total", "value": { "i64": 0 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "i32": 1 },
            "body": [{
              "op": "set",
              "name": "total",
              "value": { "call": "numeric.toI64", "args": [{ "i32": 5 }] }
            }]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }]
    }
    """;
}
