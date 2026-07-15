using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class InterpreterEntrypointFrameTests
{
    [Fact]
    public async Task Single_entrypoint_argument_populates_the_frame_directly()
    {
        var result = await ExecuteAsync(SingleParameterJson(), SandboxValue.FromInt32(42));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(42, ((I32Value)result.Value!).Value);
    }

    [Fact]
    public async Task Multiple_entrypoint_arguments_populate_the_frame_directly()
    {
        var result = await ExecuteAsync(TwoInt32ParametersJson(), Int32List(20, 22));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(42, ((I32Value)result.Value!).Value);
    }

    [Fact]
    public async Task Direct_frame_population_preserves_later_argument_validation()
    {
        var result = await ExecuteAsync(Int32AndStringParametersJson(), Int32List(20, 22));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(0, result.ResourceUsage.FuelUsed);
    }

    [Fact]
    public async Task Multiple_entrypoint_arguments_preserve_count_validation()
    {
        var result = await ExecuteAsync(TwoInt32ParametersJson(), Int32List(20));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
    }

    [Fact]
    public async Task Zero_parameter_entrypoint_accepts_unit()
    {
        var result = await ExecuteAsync(ZeroParameterJson(), SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(1, ((I32Value)result.Value!).Value);
    }

    [Fact]
    public async Task Zero_parameter_entrypoint_rejects_input_before_function_fuel()
    {
        var result = await ExecuteAsync(ZeroParameterJson(), SandboxValue.FromInt32(1));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(0, result.ResourceUsage.FuelUsed);
    }

    private static SandboxValue Int32List(params int[] values)
        => SandboxValue.FromList(values.Select(SandboxValue.FromInt32).ToArray(), SandboxType.I32);

    private static async Task<SandboxExecutionResult> ExecuteAsync(string moduleJson, SandboxValue input)
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        return await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Interpreted,
                AllowFallbackToInterpreter = false,
                SuppressSuccessfulRunSummaryAudit = true
            });
    }

    private static string ZeroParameterJson()
        => """
        {
          "id": "entrypoint-frame-zero-parameter",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [],
            "returnType": "I32",
            "body": [{ "op": "return", "value": { "i32": 1 } }]
          }]
        }
        """;

    private static string SingleParameterJson()
        => """
        {
          "id": "entrypoint-frame-single-parameter",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "value", "type": "I32" }],
            "returnType": "I32",
            "body": [{ "op": "return", "value": { "var": "value" } }]
          }]
        }
        """;

    private static string TwoInt32ParametersJson()
        => """
        {
          "id": "entrypoint-frame-two-parameters",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [
              { "name": "left", "type": "I32" },
              { "name": "right", "type": "I32" }
            ],
            "returnType": "I32",
            "body": [{
              "op": "return",
              "value": { "op": "add", "left": { "var": "left" }, "right": { "var": "right" } }
            }]
          }]
        }
        """;

    private static string Int32AndStringParametersJson()
        => """
        {
          "id": "entrypoint-frame-invalid-later-parameter",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [
              { "name": "value", "type": "I32" },
              { "name": "label", "type": "String" }
            ],
            "returnType": "I32",
            "body": [{ "op": "return", "value": { "var": "value" } }]
          }]
        }
        """;
}
