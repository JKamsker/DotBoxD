using DotBoxD.Kernels.Sandbox;
using static DotBoxD.Kernels.Tests.Compiled.Regression.Performance.LoopFastPathParity.LoopFastPathParityHarness;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.LoopFastPathParity;

/// <summary>
/// An F64 loop that assigns a non-negative value only proves its target non-negative when the
/// target was already non-negative before the loop: an empty range leaves the prior value in place.
/// </summary>
public sealed class EmptyRangeF64FactParityTests
{
    private const string Body = """
        { "op": "set", "name": "x", "value": { "f64": -4.0 } },
        { "op": "set", "name": "y", "value": { "f64": 0.0 } },
        { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "n" },
          "body": [{ "op": "set", "name": "x", "value": { "f64": 3.0 } }] },
        { "op": "forRange", "local": "j", "start": { "i32": 0 }, "end": { "i32": 5 },
          "body": [{ "op": "set", "name": "y", "value": { "call": "math.sqrt", "args": [{ "var": "x" }] } }] },
        { "op": "return", "value": { "var": "y" } }
        """;

    [Fact]
    public async Task Empty_range_does_not_prove_the_prior_target_non_negative()
    {
        var (interpreted, compiled) = await RunBothAsync(
            Module("""{ "name": "n", "type": "I32" }""", "F64", Body),
            SandboxValue.FromInt32(0));

        Assert.False(interpreted.Succeeded);
        Assert.False(compiled.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, interpreted.Error!.Code);
        Assert.Equal(interpreted.Error.Code, compiled.Error!.Code);
        Assert.Equal(interpreted.ResourceUsage.HostCalls, compiled.ResourceUsage.HostCalls);
        Assert.Equal(interpreted.ResourceUsage.LoopIterations, compiled.ResourceUsage.LoopIterations);
        Assert.Equal(interpreted.ResourceUsage.FuelUsed, compiled.ResourceUsage.FuelUsed);
    }

    [Fact]
    public async Task Non_empty_range_still_computes_the_sqrt_of_the_assigned_value()
    {
        var (interpreted, compiled) = await RunBothAsync(
            Module("""{ "name": "n", "type": "I32" }""", "F64", Body),
            SandboxValue.FromInt32(2));

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(SandboxValue.FromDouble(Math.Sqrt(3.0)), interpreted.Value);
        Assert.Equal(interpreted.Value, compiled.Value);
        Assert.Equal(interpreted.ResourceUsage.HostCalls, compiled.ResourceUsage.HostCalls);
        AssertCompiledNeverChargesLess(interpreted, compiled);
    }
}
