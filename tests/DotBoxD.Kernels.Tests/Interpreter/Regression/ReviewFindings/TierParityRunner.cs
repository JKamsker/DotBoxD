using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.ReviewFindings;

/// <summary>
/// Runs one module through the interpreter fast paths, the generic interpreter
/// (<see cref="SandboxExecutionOptions.EnableDebugTrace"/> disables fast paths) and the
/// compiled tier so tests can assert that results and fuel agree.
/// </summary>
internal static class TierParityRunner
{
    public static async Task<TierResults> RunAsync(
        string moduleJson,
        SandboxValue input,
        long fuel = 1_000_000)
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(fuel).WithMaxLoopIterations(1_000_000).Build());
        var fast = await host.ExecuteAsync(plan, "main", input, Options(ExecutionMode.Interpreted, trace: false));
        var generic = await host.ExecuteAsync(plan, "main", input, Options(ExecutionMode.Interpreted, trace: true));
        var compiled = await host.ExecuteAsync(plan, "main", input, Options(ExecutionMode.Compiled, trace: false));
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        return new TierResults(fast, generic, compiled);
    }

    private static SandboxExecutionOptions Options(ExecutionMode mode, bool trace)
        => new()
        {
            Mode = mode,
            AllowFallbackToInterpreter = false,
            EnableDebugTrace = trace,
            SuppressSuccessfulRunSummaryAudit = true
        };
}

internal sealed record TierResults(
    SandboxExecutionResult Fast,
    SandboxExecutionResult Generic,
    SandboxExecutionResult Compiled)
{
    public void AssertAllSucceededWith(SandboxValue expected)
    {
        AssertSucceededWith("generic", Generic, expected);
        AssertSucceededWith("fast", Fast, expected);
        AssertSucceededWith("compiled", Compiled, expected);
    }

    // The fast paths must charge exactly what the generic interpreter charges; the compiled tier may
    // charge more (its entry overhead) but never less.
    public void AssertFuelAgrees()
    {
        var generic = Generic.ResourceUsage.FuelUsed;
        Assert.True(
            generic == Fast.ResourceUsage.FuelUsed && Compiled.ResourceUsage.FuelUsed >= generic,
            $"fuel differs: generic={generic} fast={Fast.ResourceUsage.FuelUsed} compiled={Compiled.ResourceUsage.FuelUsed}");
    }

    private static void AssertSucceededWith(string tier, SandboxExecutionResult result, SandboxValue expected)
    {
        Assert.True(result.Succeeded, $"{tier}: {result.Error?.Code}: {result.Error?.SafeMessage}");
        Assert.True(Equals(expected, result.Value), $"{tier}: expected {expected} but got {result.Value}");
    }
}
