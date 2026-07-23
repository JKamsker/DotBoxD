using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.StraightScalarAssignments;

public sealed class ScalarAssignmentBoxedFallbackTests
{
    [Theory]
    [InlineData("I32", "i32")]
    [InlineData("I64", "i64")]
    [InlineData("F64", "f64")]
    public async Task Valid_boxed_target_retains_the_full_primitive_cascade(
        string type,
        string literalName)
    {
        using var host = SandboxTestHost.Create();
        var plan = await StraightScalarAssignmentTestSupport.PrepareAsync(
            host,
            Module(type, literalName));
        var function = Assert.Single(plan.Module.Functions);
        var layout = FunctionFrameLayout.Build(function, plan.FunctionAnalysis, plan.Bindings);
        var resultSlot = layout.GetSlot("result");

        Assert.True(layout.IsBoxedSlot(resultSlot));

        var result = await StraightScalarAssignmentTestSupport.ExecuteAsync(
            host,
            plan,
            SandboxValue.FromList([], ScalarType(type)));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(42, NumericValue(result.Value!));
        StraightScalarAssignmentTestSupport.AssertUsage(result.ResourceUsage, fuel: 9);
    }

    private static string Module(string type, string literalName)
        => $$"""
        {
          "id": "target-dispatch-{{type.ToLowerInvariant()}}-boxed",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{
              "name": "items",
              "type": { "name": "List", "arguments": ["{{type}}"] }
            }],
            "returnType": "{{type}}",
            "body": [
              {
                "op": "set",
                "name": "result",
                "value": {
                  "op": "add",
                  "left": { "{{literalName}}": 40 },
                  "right": { "{{literalName}}": 2 }
                }
              },
              {
                "op": "if",
                "condition": { "bool": false },
                "then": [{
                  "op": "set",
                  "name": "result",
                  "value": {
                    "call": "list.get",
                    "args": [{ "var": "items" }, { "i32": 0 }]
                  }
                }],
                "else": []
              },
              { "op": "return", "value": { "var": "result" } }
            ]
          }]
        }
        """;

    private static SandboxType ScalarType(string type)
        => type switch
        {
            "I32" => SandboxType.I32,
            "I64" => SandboxType.I64,
            "F64" => SandboxType.F64,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unknown scalar type")
        };

    private static double NumericValue(SandboxValue value)
        => value switch
        {
            I32Value number => number.Value,
            I64Value number => number.Value,
            F64Value number => number.Value,
            _ => throw new Xunit.Sdk.XunitException("unexpected boxed assignment value")
        };
}
