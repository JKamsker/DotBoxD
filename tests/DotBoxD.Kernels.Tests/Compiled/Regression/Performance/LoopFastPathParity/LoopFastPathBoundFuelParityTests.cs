using DotBoxD.Kernels.Sandbox;
using static DotBoxD.Kernels.Tests.Compiled.Regression.Performance.LoopFastPathParity.LoopFastPathParityHarness;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.LoopFastPathParity;

/// <summary>
/// The interpreter evaluates both forRange bounds (one fuel unit per literal or variable bound) before
/// it picks a loop fast path, so the compiled loop fast paths must charge the same bound fuel whether
/// the range is empty or not: exactly the interpreter's fuel for an empty range, never less otherwise.
/// </summary>
public sealed class LoopFastPathBoundFuelParityTests
{
    public static TheoryData<string, string, string> Shapes => new()
    {
        { "f64", "y", """{ "op": "add", "left": { "var": "y" }, "right": { "f64": 1.5 } }""" },
        { "list.count", "t", """{ "op": "add", "left": { "var": "t" }, "right": { "call": "list.count", "args": [{ "var": "xs" }] } }""" },
        { "list.count-set", "t", """{ "call": "list.count", "args": [{ "var": "xs" }] }""" },
        { "list.get", "t", """{ "op": "add", "left": { "var": "t" }, "right": { "call": "list.get", "args": [{ "var": "xs" }, { "i32": 1 }] } }""" },
        { "list.get-remainder", "t", """{ "op": "add", "left": { "var": "t" }, "right": { "call": "list.get", "args": [{ "var": "xs" }, { "op": "rem", "left": { "var": "j" }, "right": { "i32": 3 } }] } }""" },
        { "map.get", "t", """{ "op": "add", "left": { "var": "t" }, "right": { "call": "map.get", "args": [{ "var": "m" }, { "i32": 1 }] } }""" },
        { "string.length", "t", """{ "op": "add", "left": { "var": "t" }, "right": { "call": "string.length", "args": [{ "var": "s" }] } }""" },
        { "string.length-set", "t", """{ "call": "string.length", "args": [{ "var": "s" }] }""" },
        { "list.get-set", "t", """{ "call": "list.get", "args": [{ "var": "xs" }, { "i32": 1 }] }""" },
        { "map.get-set", "t", """{ "call": "map.get", "args": [{ "var": "m" }, { "i32": 1 }] }""" }
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task Loop_bounds_cost_the_same_fuel_as_the_interpreter(string shape, string target, string value)
    {
        var mismatches = new List<string>();
        foreach (var (start, end) in new[] { ("0", "0"), ("0", "1"), ("0", "3"), ("n", "n"), ("0", "n") })
        {
            var body = $$"""
                { "op": "set", "name": "n", "value": { "i32": 3 } },
                { "op": "set", "name": "y", "value": { "f64": 0.0 } },
                { "op": "set", "name": "t", "value": { "i32": 0 } },
                { "op": "forRange", "local": "j", "start": {{Bound(start)}}, "end": {{Bound(end)}},
                  "body": [{ "op": "set", "name": "{{target}}", "value": {{value}} }] },
                { "op": "return", "value": { "var": "t" } }
                """;
            var (interpreted, compiled) = await RunBothAsync(Module(Parameter(shape), "I32", body), Input(shape));

            Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
            Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
            Assert.Equal(interpreted.Value, compiled.Value);
            AssertCompiledNeverChargesLess(interpreted, compiled);

            // An empty range does no loop work, so only the bound evaluation differs from straight-line code.
            if (start == end && compiled.ResourceUsage.FuelUsed - interpreted.ResourceUsage.FuelUsed != CompiledEntrypointFuelOffset)
            {
                mismatches.Add($"{shape} {start}..{end}: interpreted {interpreted.ResourceUsage.FuelUsed}, compiled {compiled.ResourceUsage.FuelUsed}");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join("; ", mismatches));
    }

    private static string Bound(string bound)
        => bound == "n" ? """{ "var": "n" }""" : $$"""{ "i32": {{bound}} }""";

    private static string Parameter(string shape)
        => shape switch
        {
            "string.length" or "string.length-set" => """{ "name": "s", "type": "String" }""",
            "map.get" or "map.get-set" => """{ "name": "m", "type": { "name": "Map", "arguments": ["I32", "I32"] } }""",
            _ => ListParameter
        };

    private static SandboxValue Input(string shape)
        => shape switch
        {
            "string.length" or "string.length-set" => SandboxValue.FromString("abc"),
            "map.get" or "map.get-set" => SandboxValue.FromMap(
                new Dictionary<SandboxValue, SandboxValue> { [SandboxValue.FromInt32(1)] = SandboxValue.FromInt32(10) },
                SandboxType.I32,
                SandboxType.I32),
            _ => ThreeItemList
        };
}
