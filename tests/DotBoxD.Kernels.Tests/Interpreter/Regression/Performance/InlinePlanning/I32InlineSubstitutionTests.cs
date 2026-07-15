using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class I32InlineSubstitutionTests
{
    [Fact]
    public async Task Literal_substitution_does_not_fall_back_to_shadowing_caller_slot()
        => await AssertExecutionAsync(
            argumentJson: """{ "i32": 7 }""",
            commuteAddends: false,
            expected: 10);

    [Fact]
    public async Task Raw_loop_variable_substitution_keeps_fused_result_and_metering()
        => await AssertExecutionAsync(
            argumentJson: """{ "var": "i" }""",
            commuteAddends: true,
            expected: 3);

    private static async Task AssertExecutionAsync(
        string argumentJson,
        bool commuteAddends,
        int expected)
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(ModuleJson(argumentJson, commuteAddends));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithMaxLoopIterations(10)
                .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(100),
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Interpreted,
                AllowFallbackToInterpreter = false,
                SuppressSuccessfulRunSummaryAudit = true
            });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expected, ((I32Value)result.Value!).Value);
        Assert.Equal(23, result.ResourceUsage.FuelUsed);
        Assert.Equal(1, result.ResourceUsage.LoopIterations);
        Assert.Equal(0, result.ResourceUsage.AllocatedBytes);
        Assert.Equal(0, result.ResourceUsage.HostCalls);
    }

    private static string ModuleJson(string argumentJson, bool commuteAddends)
    {
        var left = commuteAddends ? """{ "i32": 3 }""" : """{ "var": "value" }""";
        var right = commuteAddends ? """{ "var": "value" }""" : """{ "i32": 3 }""";
        return $$"""
        {
          "id": "i32-inline-substitution",
          "version": "1.0.0",
          "functions": [
            {
              "id": "step",
              "visibility": "private",
              "parameters": [{ "name": "value", "type": "I32" }],
              "returnType": "I32",
              "body": [{
                "op": "return",
                "value": {
                  "op": "rem",
                  "left": { "op": "add", "left": {{left}}, "right": {{right}} },
                  "right": { "i32": 1000003 }
                }
              }]
            },
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "value", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "result", "value": { "i32": 0 } },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "i32": 1 },
                  "body": [{
                    "op": "set",
                    "name": "result",
                    "value": { "call": "step", "args": [{{argumentJson}}] }
                  }]
                },
                { "op": "return", "value": { "var": "result" } }
              ]
            }
          ]
        }
        """;
    }
}
