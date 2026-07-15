using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class I32SingleAssignmentLoopFallbackTests
{
    [Fact]
    public async Task Non_i32_single_assignment_uses_the_general_loop_path()
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
        Assert.Equal(5, ((I32Value)result.Value!).Value);
        Assert.Equal(1, result.ResourceUsage.LoopIterations);
    }

    private const string ModuleJson = """
    {
      "id": "i32-single-statement-loop-fallback",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "label", "value": { "string": "before" } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "i32": 1 },
            "body": [{ "op": "set", "name": "label", "value": { "string": "after" } }]
          },
          { "op": "return", "value": { "call": "string.length", "args": [{ "var": "label" }] } }
        ]
      }]
    }
    """;
}
