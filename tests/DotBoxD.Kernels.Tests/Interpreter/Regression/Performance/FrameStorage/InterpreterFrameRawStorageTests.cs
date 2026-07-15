using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class InterpreterFrameRawStorageTests
{
    [Fact]
    public async Task I64_parameter_return_executes_with_an_all_raw_layout()
    {
        var result = await ExecutePreparedAsync(I64ParameterJson(), SandboxValue.FromInt64(42));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(42, ((I64Value)result.Value!).Value);
    }

    [Fact]
    public async Task Mixed_raw_and_boxed_layout_keeps_boxed_values_available()
    {
        var result = await ExecutePreparedAsync(MixedRawAndBoxedJson(), SandboxValue.FromInt32(9));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(19, ((I32Value)result.Value!).Value);
    }

    [Fact]
    public async Task Wrong_kind_list_fast_path_falls_back_to_a_sandbox_error()
    {
        using var host = SandboxTestHost.Create();
        var validModule = await host.ImportJsonAsync(ValidListCountJson());
        var prepared = await host.PrepareAsync(validModule, Policy());
        var tamperedModule = await host.ImportJsonAsync(WrongKindListCountJson());
        var tampered = new ExecutionPlan(
            prepared.ModuleHash,
            prepared.PlanHash,
            prepared.PlanSeal,
            prepared.PolicyHash,
            prepared.BindingManifestHash,
            tamperedModule,
            prepared.Policy,
            prepared.Bindings,
            prepared.Budget,
            prepared.FunctionAnalysis,
            prepared.BindingReferences);
        var interpreter = new SandboxInterpreter();

        var result = await interpreter.ExecuteAsync(
            tampered,
            "main",
            SandboxValue.FromInt32(7),
            Options(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotEqual(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error.Code);
    }

    private static async Task<SandboxExecutionResult> ExecutePreparedAsync(string moduleJson, SandboxValue input)
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, Policy());
        return await host.ExecuteAsync(plan, "main", input, Options());
    }

    private static SandboxPolicy Policy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(1_000)
            .WithMaxLoopIterations(10)
            .Build();

    private static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

    private static string I64ParameterJson()
        => """
        {
          "id": "frame-storage-i64",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "value", "type": "I64" }],
            "returnType": "I64",
            "body": [{ "op": "return", "value": { "var": "value" } }]
          }]
        }
        """;

    private static string MixedRawAndBoxedJson()
        => """
        {
          "id": "frame-storage-mixed",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "value", "type": "I32" }],
            "returnType": "I32",
            "body": [
              { "op": "set", "name": "label", "value": { "string": "kept boxed" } },
              {
                "op": "return",
                "value": {
                  "op": "add",
                  "left": { "var": "value" },
                  "right": { "call": "string.length", "args": [{ "var": "label" }] }
                }
              }
            ]
          }]
        }
        """;

    private static string ValidListCountJson()
        => """
        {
          "id": "frame-storage-valid-list",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "value", "type": { "name": "List", "arguments": ["I32"] } }],
            "returnType": "I32",
            "body": [
              { "op": "set", "name": "result", "value": { "i32": 0 } },
              {
                "op": "forRange",
                "local": "i",
                "start": { "i32": 0 },
                "end": { "i32": 1 },
                "body": [{
                  "op": "set",
                  "name": "result",
                  "value": { "call": "list.count", "args": [{ "var": "value" }] }
                }]
              },
              { "op": "return", "value": { "var": "result" } }
            ]
          }]
        }
        """;

    private static string WrongKindListCountJson()
        => """
        {
          "id": "frame-storage-wrong-kind",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "value", "type": "I32" }],
            "returnType": "I32",
            "body": [
              { "op": "set", "name": "result", "value": { "i32": 0 } },
              {
                "op": "forRange",
                "local": "i",
                "start": { "i32": 0 },
                "end": { "i32": 1 },
                "body": [{
                  "op": "set",
                  "name": "result",
                  "value": { "call": "list.count", "args": [{ "var": "value" }] }
                }]
              },
              { "op": "return", "value": { "var": "result" } }
            ]
          }]
        }
        """;
}
