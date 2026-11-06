using DotBoxD.Kernels.Sandbox;
using static DotBoxD.Kernels.Tests.Compiled.Regression.Performance.LoopFastPathParity.LoopFastPathParityHarness;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.LoopFastPathParity;

/// <summary>
/// A non-negative F64 fact learned while compiling the then-branch must not leak into the else-branch:
/// at runtime only one branch executes, so the else-branch still sees the pre-if value.
/// </summary>
public sealed class IfBranchF64FactParityTests
{
    [Fact]
    public async Task Then_branch_non_negative_fact_does_not_enable_else_branch_sqrt_fast_path()
    {
        const string body = """
            { "op": "set", "name": "x", "value": { "f64": -4.0 } },
            { "op": "set", "name": "y", "value": { "f64": 0.0 } },
            { "op": "if", "condition": { "var": "c" },
              "then": [{ "op": "set", "name": "x", "value": { "f64": 4.0 } }],
              "else": [{ "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "i32": 3 },
                "body": [{ "op": "set", "name": "y", "value": { "call": "math.sqrt", "args": [{ "var": "x" }] } }] }] },
            { "op": "return", "value": { "var": "y" } }
            """;

        var (interpreted, compiled) = await RunBothAsync(
            Module("""{ "name": "c", "type": "Bool" }""", "F64", body),
            SandboxValue.FromBool(false));

        Assert.False(interpreted.Succeeded);
        Assert.False(compiled.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, interpreted.Error!.Code);
        Assert.Equal(interpreted.Error.Code, compiled.Error!.Code);
        Assert.Equal(interpreted.ResourceUsage.HostCalls, compiled.ResourceUsage.HostCalls);
        Assert.Equal(interpreted.ResourceUsage.LoopIterations, compiled.ResourceUsage.LoopIterations);
        Assert.Equal(interpreted.ResourceUsage.FuelUsed, compiled.ResourceUsage.FuelUsed);
    }
}
