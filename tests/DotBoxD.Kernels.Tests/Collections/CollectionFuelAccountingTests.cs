using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Collections;

public sealed class CollectionFuelAccountingTests
{
    public static TheoryData<ExecutionMode> Modes()
        => new()
        {
            ExecutionMode.Interpreted,
            ExecutionMode.Compiled
        };

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task List_add_charges_source_size_fuel(ExecutionMode mode)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync("""
        {
          "id": "collection-fuel",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [
                { "name": "items", "type": { "name": "List", "arguments": ["I32"] } }
              ],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "list.count",
                    "args": [
                      {
                        "call": "list.add",
                        "args": [{ "var": "items" }, { "i32": 999 }]
                      }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(10_000)
                .Build());
        var input = SandboxValue.FromList(
            Enumerable.Range(0, 80).Select(SandboxValue.FromInt32).ToArray(),
            SandboxType.I32);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(81, ((I32Value)result.Value!).Value);
        Assert.True(result.ResourceUsage.FuelUsed >= 80);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [InlineData("eq")]
    [InlineData("ne")]
    public async Task Nested_list_equality_charges_fuel_per_compared_element_in_both_tiers(string op)
    {
        var small = await RunEqualityAsync(op, NestedListType, NestedList(4));
        var large = await RunEqualityAsync(op, NestedListType, NestedList(40));

        // Outer count plus every inner element is compared: 40 + 40*40 vs 4 + 4*4.
        Assert.True(
            large.Interpreted - small.Interpreted >= (40 + (40 * 40)) - (4 + (4 * 4)),
            $"interpreted fuel did not scale: small={small.Interpreted} large={large.Interpreted}");
        AssertTiersAgree(small, large);
    }

    [Fact]
    public async Task String_equality_charges_fuel_per_character_in_both_tiers()
    {
        var small = await RunEqualityAsync("eq", "\"String\"", SandboxValue.FromString(new string('a', 8)));
        var large = await RunEqualityAsync("eq", "\"String\"", SandboxValue.FromString(new string('a', 4_008)));

        Assert.True(large.Interpreted - small.Interpreted >= 4_000);
        AssertTiersAgree(small, large);
    }

    // Both tiers charge the same per-element equality fuel; the compiled tier may add a constant entry
    // overhead but never charges less.
    private static void AssertTiersAgree(
        (long Interpreted, long Compiled) small,
        (long Interpreted, long Compiled) large)
    {
        Assert.Equal(large.Interpreted - small.Interpreted, large.Compiled - small.Compiled);
        Assert.True(small.Compiled >= small.Interpreted, $"compiled {small.Compiled} < interpreted {small.Interpreted}");
    }

    private const string NestedListType
        = """{ "name": "List", "arguments": [{ "name": "List", "arguments": ["I32"] }] }""";

    private static SandboxValue NestedList(int size)
        => SandboxValue.FromList(
            Enumerable.Range(0, size)
                .Select(_ => SandboxValue.FromList(
                    Enumerable.Range(0, size).Select(SandboxValue.FromInt32).ToArray(),
                    SandboxType.I32))
                .ToArray(),
            SandboxType.List(SandboxType.I32));

    private static async Task<(long Interpreted, long Compiled)> RunEqualityAsync(
        string op,
        string parameterType,
        SandboxValue input)
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync($$"""
        {
          "id": "equality-fuel",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "value", "type": {{parameterType}} }],
            "returnType": "Bool",
            "body": [{
              "op": "return",
              "value": { "op": "{{op}}", "left": { "var": "value" }, "right": { "var": "value" } }
            }]
          }]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000_000).Build());
        var interpreted = await host.ExecuteAsync(
            plan, "main", input, new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });
        var compiled = await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        Assert.Equal(SandboxValue.FromBool(op == "eq"), interpreted.Value);
        Assert.Equal(interpreted.Value, compiled.Value);
        return (interpreted.ResourceUsage.FuelUsed, compiled.ResourceUsage.FuelUsed);
    }
}
