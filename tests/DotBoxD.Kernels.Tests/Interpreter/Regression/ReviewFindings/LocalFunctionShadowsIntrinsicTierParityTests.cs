using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.ReviewFindings;

/// <summary>
/// Validation resolves a call to a module function before a binding of the same id, so every tier -
/// including the compiled raw-intrinsic and loop fast paths - must call the module function.
/// </summary>
public sealed class LocalFunctionShadowsIntrinsicTierParityTests
{
    private const string F64Parameter = """{ "name": "x", "type": "F64" }""";
    private const string I32Parameter = """{ "name": "x", "type": "I32" }""";

    [Fact]
    public async Task Local_math_abs_wins_for_an_i32_local()
    {
        var results = await TierParityRunner.RunAsync(
            Module(
                Helper("math.abs", I32Parameter, "I32", """{ "op": "add", "left": { "var": "x" }, "right": { "i32": 100 } }"""),
                "I32",
                """{ "op": "set", "name": "x", "value": { "call": "math.abs", "args": [{ "i32": -5 }] } }""",
                """{ "op": "return", "value": { "var": "x" } }"""),
            SandboxValue.Unit);

        results.AssertAllSucceededWith(SandboxValue.FromInt32(95));
        results.AssertFuelAgrees();
    }

    [Fact]
    public async Task Local_math_sqrt_wins_for_a_raw_f64_return()
    {
        var results = await TierParityRunner.RunAsync(
            Module(
                SqrtPlusOneHelper,
                "F64",
                """{ "op": "return", "value": { "call": "math.sqrt", "args": [{ "f64": 4.0 }] } }"""),
            SandboxValue.Unit);

        results.AssertAllSucceededWith(SandboxValue.FromDouble(5.0));
        results.AssertFuelAgrees();
    }

    [Fact]
    public async Task Local_math_sqrt_wins_inside_an_f64_loop()
    {
        var results = await TierParityRunner.RunAsync(
            Module(
                SqrtPlusOneHelper,
                "F64",
                """{ "op": "set", "name": "t", "value": { "f64": 4.0 } }""",
                Loop("""{ "op": "set", "name": "t", "value": { "call": "math.sqrt", "args": [{ "var": "t" }] } }"""),
                """{ "op": "return", "value": { "var": "t" } }"""),
            SandboxValue.Unit);

        results.AssertAllSucceededWith(SandboxValue.FromDouble(7.0));
        results.AssertFuelAgrees();
    }

    [Fact]
    public async Task Local_string_length_wins_inside_an_i32_loop()
    {
        var results = await TierParityRunner.RunAsync(
            Module(
                Helper("string.length", """{ "name": "s", "type": "String" }""", "I32", """{ "i32": 7 }"""),
                "I32",
                """{ "op": "set", "name": "s", "value": { "string": "ab" } }""",
                """{ "op": "set", "name": "n", "value": { "i32": 0 } }""",
                Loop("""
                    {
                      "op": "set", "name": "n",
                      "value": { "op": "add", "left": { "var": "n" }, "right": { "call": "string.length", "args": [{ "var": "s" }] } }
                    }
                    """),
                """{ "op": "return", "value": { "var": "n" } }"""),
            SandboxValue.Unit);

        results.AssertAllSucceededWith(SandboxValue.FromInt32(21));
        results.AssertFuelAgrees();
    }

    [Fact]
    public async Task Local_math_min_wins_inside_an_i32_loop()
    {
        var results = await TierParityRunner.RunAsync(
            Module(
                Helper(
                    "math.min",
                    """{ "name": "a", "type": "I32" }, { "name": "b", "type": "I32" }""",
                    "I32",
                    """{ "op": "add", "left": { "var": "a" }, "right": { "var": "b" } }"""),
                "I32",
                """{ "op": "set", "name": "n", "value": { "i32": 0 } }""",
                Loop("""{ "op": "set", "name": "n", "value": { "call": "math.min", "args": [{ "var": "n" }, { "i32": 10 }] } }"""),
                """{ "op": "return", "value": { "var": "n" } }"""),
            SandboxValue.Unit);

        results.AssertAllSucceededWith(SandboxValue.FromInt32(30));
        results.AssertFuelAgrees();
    }

    [Fact]
    public async Task Local_math_floor_does_not_prove_its_result_non_negative()
    {
        // The local math.floor negates its argument, so the real math.sqrt in the loop must fail on its
        // first call; a compiled non-negative fact would bulk-charge every iteration's host call first.
        var results = await TierParityRunner.RunAsync(
            Module(
                Helper("math.floor", F64Parameter, "F64", """{ "op": "sub", "left": { "f64": 0.0 }, "right": { "var": "x" } }"""),
                "F64",
                """{ "op": "set", "name": "x", "value": { "call": "math.floor", "args": [{ "f64": 4.0 }] } }""",
                """{ "op": "set", "name": "y", "value": { "f64": 0.0 } }""",
                Loop("""{ "op": "set", "name": "y", "value": { "call": "math.sqrt", "args": [{ "var": "x" }] } }"""),
                """{ "op": "return", "value": { "var": "y" } }"""),
            SandboxValue.Unit);

        foreach (var (tier, result) in new[] { ("fast", results.Fast), ("compiled", results.Compiled) })
        {
            Assert.False(results.Generic.Succeeded);
            Assert.False(result.Succeeded, tier);
            Assert.Equal(results.Generic.Error!.Code, result.Error!.Code);
            Assert.True(
                results.Generic.ResourceUsage.HostCalls == result.ResourceUsage.HostCalls,
                $"{tier}: host calls {result.ResourceUsage.HostCalls}, generic {results.Generic.ResourceUsage.HostCalls}");
        }
    }

    private static string SqrtPlusOneHelper
        => Helper("math.sqrt", F64Parameter, "F64", """{ "op": "add", "left": { "var": "x" }, "right": { "f64": 1.0 } }""");

    private static string Loop(string bodyStatement)
        => $$"""
        {
          "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "i32": 3 },
          "body": [{{bodyStatement}}]
        }
        """;

    private static string Helper(string id, string parameters, string returnType, string result)
        => $$"""
        {
          "id": "{{id}}",
          "visibility": "private",
          "parameters": [{{parameters}}],
          "returnType": "{{returnType}}",
          "body": [{ "op": "return", "value": {{result}} }]
        }
        """;

    private static string Module(string helper, string mainReturnType, params string[] mainBody)
        => $$"""
        {
          "id": "local-shadows-intrinsic-tiers",
          "version": "1.0.0",
          "functions": [
            {{helper}},
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
