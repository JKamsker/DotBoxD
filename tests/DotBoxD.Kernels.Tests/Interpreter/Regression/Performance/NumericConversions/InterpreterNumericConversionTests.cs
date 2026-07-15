using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class InterpreterNumericConversionTests
{
    [Theory]
    [InlineData("numeric.toI64", "I32", "I64", false)]
    [InlineData("numeric.toF64", "I32", "F64", false)]
    [InlineData("numeric.toF64", "I64", "F64", true)]
    public async Task Direct_numeric_conversion_preserves_value_and_resource_accounting(
        string conversion,
        string sourceType,
        string targetType,
        bool sourceIsI64)
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(ConversionModule(conversion, sourceType, targetType));
        var plan = await host.PrepareAsync(module, Policy());
        var input = sourceIsI64
            ? SandboxValue.FromInt64(-1_000)
            : SandboxValue.FromInt32(-1_000);

        var result = await ExecuteInterpretedAsync(host, plan, input);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Equal(-1_000.0, NumericValue(result.Value!));
        Assert.Equal(8, result.ResourceUsage.FuelUsed);
        Assert.Equal(0, result.ResourceUsage.LoopIterations);
        Assert.Equal(0, result.ResourceUsage.AllocatedBytes);
        Assert.Equal(0, result.ResourceUsage.HostCalls);
    }

    [Fact]
    public async Task Numeric_conversion_awaits_an_asynchronous_operand()
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(DelayedI32Binding());
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync(AsyncOperandModule("test.delayedI32"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .AllowRuntimeAsync()
                .WithFuel(1_000)
                .Build());

        var result = await ExecuteInterpretedAsync(host, plan, SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(42, ((I64Value)result.Value!).Value);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
    }

    [Fact]
    public async Task Numeric_conversion_propagates_an_asynchronous_operand_failure()
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(FailingI32Binding());
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync(AsyncOperandModule("test.failingI32"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .AllowRuntimeAsync()
                .WithFuel(1_000)
                .Build());

        var result = await ExecuteInterpretedAsync(host, plan, SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
    }

    [Theory]
    [InlineData("numeric.toI64", "I64")]
    [InlineData("numeric.toF64", "F64")]
    public async Task Numeric_conversion_wrong_operand_type_keeps_validation_diagnostic(
        string conversion,
        string targetType)
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(InvalidModule(
            conversion,
            targetType,
            """[{ "string": "wrong" }]"""));

        var exception = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, Policy()));

        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Code == "E-TYPE-MISMATCH");
    }

    [Theory]
    [InlineData("numeric.toI64", "I64", "[]")]
    [InlineData("numeric.toI64", "I64", "[{ \"i32\": 1 }, { \"i32\": 2 }]")]
    [InlineData("numeric.toF64", "F64", "[]")]
    [InlineData("numeric.toF64", "F64", "[{ \"i32\": 1 }, { \"i32\": 2 }]")]
    public async Task Numeric_conversion_malformed_arity_keeps_validation_diagnostic(
        string conversion,
        string targetType,
        string argumentsJson)
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(InvalidModule(conversion, targetType, argumentsJson));

        var exception = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, Policy()));

        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Code == "E-CALL-ARITY");
    }

    private static async ValueTask<SandboxValue> DelayedValueAsync()
    {
        await Task.Yield();
        return SandboxValue.FromInt32(42);
    }

    private static async ValueTask<SandboxValue> DelayedFailureAsync()
    {
        await Task.Yield();
        throw new InvalidOperationException("delayed test failure");
    }

    private static BindingDescriptor DelayedI32Binding()
        => AsyncI32Binding("test.delayedI32", DelayedValueAsync);

    private static BindingDescriptor FailingI32Binding()
        => AsyncI32Binding("test.failingI32", DelayedFailureAsync);

    private static BindingDescriptor AsyncI32Binding(
        string id,
        Func<ValueTask<SandboxValue>> invoke)
        => new(
            id,
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => invoke(),
            CompiledBinding.RuntimeStub(
                typeof(CompiledRuntime).FullName!,
                nameof(CompiledRuntime.CallBinding)))
        {
            IsAsync = true
        };

    private static double NumericValue(SandboxValue value)
        => value switch
        {
            I64Value number => number.Value,
            F64Value number => number.Value,
            _ => throw new Xunit.Sdk.XunitException("unexpected numeric conversion result")
        };

    private static async Task<SandboxExecutionResult> ExecuteInterpretedAsync(
        SandboxHost host,
        ExecutionPlan plan,
        SandboxValue input)
        => await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Interpreted,
                AllowFallbackToInterpreter = false,
                SuppressSuccessfulRunSummaryAudit = true
            });

    private static SandboxPolicy Policy()
        => SandboxPolicyBuilder.Create().WithFuel(1_000).Build();

    private static string ConversionModule(string conversion, string sourceType, string targetType)
    {
        var targetLiteral = targetType == "I64" ? "i64" : "f64";
        return $$"""
        {
          "id": "interpreter-direct-{{sourceType.ToLowerInvariant()}}-{{targetType.ToLowerInvariant()}}",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "input", "type": "{{sourceType}}" }],
            "returnType": "{{targetType}}",
            "body": [
              { "op": "set", "name": "seed", "value": { "{{targetLiteral}}": 1000 } },
              {
                "op": "set",
                "name": "converted",
                "value": { "call": "{{conversion}}", "args": [{ "var": "input" }] }
              },
              { "op": "return", "value": { "var": "converted" } }
            ]
          }]
        }
        """;
    }

    private static string InvalidModule(string conversion, string targetType, string argumentsJson)
        => $$"""
        {
          "id": "interpreter-invalid-numeric-conversion",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [],
            "returnType": "{{targetType}}",
            "body": [{
              "op": "return",
              "value": { "call": "{{conversion}}", "args": {{argumentsJson}} }
            }]
          }]
        }
        """;

    private static string AsyncOperandModule(string binding)
        => $$"""
    {
      "id": "interpreter-async-numeric-conversion",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I64",
        "body": [{
          "op": "return",
          "value": {
            "call": "numeric.toI64",
            "args": [{ "call": "{{binding}}", "args": [] }]
          }
        }]
      }]
    }
    """;
}
