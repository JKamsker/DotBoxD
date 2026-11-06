using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.ReviewFindings;

/// <summary>
/// A loop variable whose name is also assigned a non-i32 value elsewhere in the function is stored
/// in a boxed frame slot. Loop fast paths must not write it through the raw i32 slot array.
/// </summary>
public sealed class BoxedLoopSlotFastPathTests
{
    [Fact]
    public async Task Sequential_i32_loops_over_boxed_loop_slot_match_generic_and_compiled()
    {
        var results = await TierParityRunner.RunAsync(
            Module(
                """{ "i32": 0 }""",
                """{ "op": "add", "left": { "var": "s" }, "right": { "var": "i" } }"""),
            SandboxValue.Unit);

        results.AssertAllSucceededWith(SandboxValue.FromInt32(6));
        results.AssertFuelAgrees();
    }

    [Fact]
    public async Task F64_loop_over_only_boxed_i32_like_slot_does_not_host_fail()
    {
        var results = await TierParityRunner.RunAsync(
            Module(
                """{ "f64": 0.0 }""",
                """{ "op": "add", "left": { "var": "s" }, "right": { "f64": 1.5 } }"""),
            SandboxValue.Unit);

        results.AssertAllSucceededWith(SandboxValue.FromDouble(9.0));
        results.AssertFuelAgrees();
    }

    private static string Module(string seed, string step)
        => $$"""
        {
          "id": "boxed-loop-slot",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [],
            "returnType": "{{(seed.Contains("f64", StringComparison.Ordinal) ? "F64" : "I32")}}",
            "body": [
              { "op": "set", "name": "s", "value": {{seed}} },
              {
                "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "i32": 3 },
                "body": [{ "op": "set", "name": "s", "value": {{step}} }]
              },
              {
                "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "i32": 3 },
                "body": [{ "op": "set", "name": "s", "value": {{step}} }]
              },
              { "op": "set", "name": "i", "value": { "string": "x" } },
              { "op": "return", "value": { "var": "s" } }
            ]
          }]
        }
        """;
}
