using DotBoxD.Kernels.Sandbox;
using static DotBoxD.Kernels.Tests.Compiled.Regression.Performance.LoopFastPathParity.LoopFastPathParityHarness;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.LoopFastPathParity;

/// <summary>
/// The single-assignment collection loop fast paths may collapse the body into a closed form, but the
/// loop variable must still end at <c>end - 1</c> (or keep its prior value for an empty range), and an
/// accumulator that is the loop variable itself must observe the per-iteration loop-variable write.
/// </summary>
public sealed class LoopFastPathLoopVariableParityTests
{
    public static TheoryData<string, string> Sources => new()
    {
        { "list.count", """{ "call": "list.count", "args": [{ "var": "xs" }] }""" },
        { "string.length", """{ "call": "string.length", "args": [{ "var": "s" }] }""" },
        { "list.get-remainder", """{ "call": "list.get", "args": [{ "var": "xs" }, { "op": "rem", "left": { "var": "i" }, "right": { "i32": 3 } }] }""" },
        { "list.get-literal", """{ "call": "list.get", "args": [{ "var": "xs" }, { "i32": 1 }] }""" },
        { "map.get", """{ "call": "map.get", "args": [{ "var": "m" }, { "i32": 1 }] }""" }
    };

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task Accumulating_into_the_loop_variable_matches_interpreter(string shape, string source)
    {
        var body = $$"""
            { "op": "set", "name": "i", "value": { "i32": 0 } },
            { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "i32": 5 },
              "body": [{ "op": "set", "name": "i", "value": { "op": "add", "left": { "var": "i" }, "right": {{source}} } }] },
            { "op": "return", "value": { "var": "i" } }
            """;

        await AssertSuccessfulParityAsync(Module(Parameter(shape), "I32", body), Input(shape), ExpectedSelfAccumulation(shape));
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task Loop_variable_ends_at_last_index_after_accumulation(string shape, string source)
    {
        var body = $$"""
            { "op": "set", "name": "i", "value": { "i32": 100 } },
            { "op": "set", "name": "t", "value": { "i32": 0 } },
            { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "i32": 5 },
              "body": [{ "op": "set", "name": "t", "value": { "op": "add", "left": { "var": "t" }, "right": {{source}} } }] },
            { "op": "return", "value": { "var": "i" } }
            """;

        await AssertSuccessfulParityAsync(Module(Parameter(shape), "I32", body), Input(shape), 4);
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task Loop_variable_is_untouched_by_an_empty_range(string shape, string source)
    {
        var body = $$"""
            { "op": "set", "name": "i", "value": { "i32": 100 } },
            { "op": "set", "name": "t", "value": { "i32": 0 } },
            { "op": "forRange", "local": "i", "start": { "i32": 5 }, "end": { "i32": 5 },
              "body": [{ "op": "set", "name": "t", "value": { "op": "add", "left": { "var": "t" }, "right": {{source}} } }] },
            { "op": "return", "value": { "var": "i" } }
            """;

        await AssertSuccessfulParityAsync(Module(Parameter(shape), "I32", body), Input(shape), 100);
    }

    private static int ExpectedSelfAccumulation(string shape)
        => shape switch
        {
            // Each iteration rewrites i, so only the last iteration (i = 4) survives.
            "list.count" or "string.length" => 4 + 3,
            "list.get-remainder" => 4 + 2, // xs[4 % 3]
            "list.get-literal" => 4 + 2, // xs[1]
            "map.get" => 4 + 10, // m[1]
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

    private static string Parameter(string shape)
        => shape switch
        {
            "string.length" => """{ "name": "s", "type": "String" }""",
            "map.get" => """{ "name": "m", "type": { "name": "Map", "arguments": ["I32", "I32"] } }""",
            _ => ListParameter
        };

    private static SandboxValue Input(string shape)
        => shape switch
        {
            "string.length" => SandboxValue.FromString("abc"),
            "map.get" => SandboxValue.FromMap(
                new Dictionary<SandboxValue, SandboxValue>
                {
                    [SandboxValue.FromInt32(1)] = SandboxValue.FromInt32(10),
                    [SandboxValue.FromInt32(2)] = SandboxValue.FromInt32(20)
                },
                SandboxType.I32,
                SandboxType.I32),
            _ => ThreeItemList
        };
}
