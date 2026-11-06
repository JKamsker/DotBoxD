using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.ReviewFindings;

/// <summary>
/// Validation resolves a call to a module-local function before a binding of the same id, so the
/// interpreter must too, including its intrinsic and loop fast paths.
/// </summary>
public sealed class LocalFunctionShadowsIntrinsicTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Local_math_abs_wins_over_the_intrinsic(bool debugTrace)
    {
        var result = await RunInterpretedAsync(
            Module(
                "I32",
                """{ "name": "x", "type": "I32" }""",
                "I32",
                """{ "op": "add", "left": { "var": "x" }, "right": { "i32": 100 } }""",
                """{ "op": "return", "value": { "call": "math.abs", "args": [{ "i32": -5 }] } }"""),
            debugTrace);

        Assert.Equal(SandboxValue.FromInt32(95), result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Local_math_sqrt_wins_inside_an_f64_loop(bool debugTrace)
    {
        var result = await RunInterpretedAsync(
            Module(
                "F64",
                """{ "name": "x", "type": "F64" }""",
                "F64",
                """{ "op": "add", "left": { "var": "x" }, "right": { "f64": 1.0 } }""",
                """{ "op": "set", "name": "t", "value": { "f64": 4.0 } }""",
                """
                {
                  "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "i32": 3 },
                  "body": [{ "op": "set", "name": "t", "value": { "call": "math.sqrt", "args": [{ "var": "t" }] } }]
                }
                """,
                """{ "op": "return", "value": { "var": "t" } }"""),
            debugTrace);

        Assert.Equal(SandboxValue.FromDouble(7.0), result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Local_string_length_wins_inside_an_i32_loop(bool debugTrace)
    {
        var result = await RunInterpretedAsync(
            Module(
                "I32",
                """{ "name": "s", "type": "String" }""",
                "I32",
                """{ "i32": 7 }""",
                """{ "op": "set", "name": "s", "value": { "string": "ab" } }""",
                """{ "op": "set", "name": "n", "value": { "i32": 0 } }""",
                """
                {
                  "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "i32": 3 },
                  "body": [{
                    "op": "set", "name": "n",
                    "value": { "op": "add", "left": { "var": "n" }, "right": { "call": "string.length", "args": [{ "var": "s" }] } }
                  }]
                }
                """,
                """{ "op": "return", "value": { "var": "n" } }"""),
            debugTrace);

        Assert.Equal(SandboxValue.FromInt32(21), result);
    }

    private static async Task<SandboxValue?> RunInterpretedAsync(string moduleJson, bool debugTrace)
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(10_000).Build());
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Interpreted,
                AllowFallbackToInterpreter = false,
                EnableDebugTrace = debugTrace
            });

        Assert.True(result.Succeeded, result.Error?.Code + ": " + result.Error?.SafeMessage);
        return result.Value;
    }

    private static string Module(
        string mainReturnType,
        string helperParameter,
        string helperReturnType,
        string helperResult,
        params string[] mainBody)
    {
        var name = helperParameter.Contains("String", StringComparison.Ordinal)
            ? "string.length"
            : helperReturnType == "F64" ? "math.sqrt" : "math.abs";
        return $$"""
        {
          "id": "local-shadows-intrinsic",
          "version": "1.0.0",
          "functions": [
            {
              "id": "{{name}}",
              "visibility": "private",
              "parameters": [{{helperParameter}}],
              "returnType": "{{helperReturnType}}",
              "body": [{ "op": "return", "value": {{helperResult}} }]
            },
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "{{mainReturnType}}",
              "body": [{{string.Join(",", mainBody)}}]
            }
          ]
        }
        """;
    }
}
