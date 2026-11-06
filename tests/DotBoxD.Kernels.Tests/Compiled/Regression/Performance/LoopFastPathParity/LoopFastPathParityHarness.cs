using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.LoopFastPathParity;

/// <summary>
/// Runs one single-entrypoint module in interpreted and compiled mode so loop fast-path tests can
/// assert exact result and resource-accounting parity.
/// </summary>
internal static class LoopFastPathParityHarness
{
    // Successful compiled entrypoints spend one extra generated-call fuel unit before any statement.
    public const long CompiledEntrypointFuelOffset = 1;

    public static readonly SandboxValue ThreeItemList = SandboxValue.FromList(
        [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2), SandboxValue.FromInt32(3)],
        SandboxType.I32);

    public const string ListParameter = """{ "name": "xs", "type": { "name": "List", "arguments": ["I32"] } }""";

    public static string Module(string parameter, string returnType, string body)
        => $$"""
        {
          "id": "loop-fast-path-parity",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{{parameter}}],
            "returnType": "{{returnType}}",
            "body": [{{body}}]
          }]
        }
        """;

    public static async Task<(SandboxExecutionResult Interpreted, SandboxExecutionResult Compiled)> RunBothAsync(
        string moduleJson,
        SandboxValue input)
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(100_000)
                .WithMaxLoopIterations(10_000)
                .Build());

        var interpreted = await host.ExecuteAsync(plan, "main", input, Options(ExecutionMode.Interpreted));
        var compiled = await host.ExecuteAsync(plan, "main", input, Options(ExecutionMode.Compiled));
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        return (interpreted, compiled);
    }

    public static async Task AssertSuccessfulParityAsync(string moduleJson, SandboxValue input, int expected)
    {
        var (interpreted, compiled) = await RunBothAsync(moduleJson, input);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(expected, ((I32Value)interpreted.Value!).Value);
        Assert.Equal(interpreted.Value, compiled.Value);
        Assert.Equal(interpreted.ResourceUsage.LoopIterations, compiled.ResourceUsage.LoopIterations);
        AssertCompiledNeverChargesLess(interpreted, compiled);
    }

    // Compiled fast paths may charge conservatively more (e.g. for materializing a raw list reader), but
    // never less than the interpreter.
    public static void AssertCompiledNeverChargesLess(SandboxExecutionResult interpreted, SandboxExecutionResult compiled)
        => Assert.True(
            compiled.ResourceUsage.FuelUsed - interpreted.ResourceUsage.FuelUsed >= CompiledEntrypointFuelOffset,
            $"interpreted fuel {interpreted.ResourceUsage.FuelUsed}, compiled fuel {compiled.ResourceUsage.FuelUsed}");

    private static SandboxExecutionOptions Options(ExecutionMode mode)
        => new() { Mode = mode, AllowFallbackToInterpreter = false };
}
