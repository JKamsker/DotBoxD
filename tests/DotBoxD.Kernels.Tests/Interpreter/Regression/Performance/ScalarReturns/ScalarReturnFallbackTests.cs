using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.ScalarReturns;

public sealed class ScalarReturnFallbackTests
{
    [Theory]
    [InlineData("I64", "i64", "2", "3", 10.0)]
    [InlineData("F64", "f64", "2.0", "3.0", 10.0)]
    public async Task Debug_trace_keeps_generic_preorder_expression_events(
        string type,
        string literalName,
        string left,
        string right,
        double expected)
    {
        var expression = $$"""
        {
          "op": "add",
          "left": { "var": "value" },
          "right": {
            "op": "mul",
            "left": { "{{literalName}}": {{left}} },
            "right": { "{{literalName}}": {{right}} }
          }
        }
        """;
        using var host = SandboxTestHost.Create();
        var plan = await ScalarReturnTestSupport.PrepareAsync(
            host,
            ScalarReturnTestModules.Expression("scalar-return-debug-fallback", type, expression));

        var result = await ScalarReturnTestSupport.ExecuteAsync(
            host,
            plan,
            ScalarReturnTestSupport.Scalar(type, 4),
            enableDebugTrace: true);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expected, ScalarReturnTestSupport.NumericValue(result.Value));
        ScalarReturnTestSupport.AssertUsage(result.ResourceUsage, fuel: 7);
        var traces = result.AuditEvents.Where(audit => audit.Kind == "DebugTrace").ToArray();
        Assert.Equal(
            [
                "statement:ReturnStatement",
                "expression:BinaryExpression",
                "expression:VariableExpression",
                "expression:BinaryExpression",
                "expression:LiteralExpression",
                "expression:LiteralExpression"
            ],
            traces.Select(Node).ToArray());
        Assert.Equal(
            ["998", "997", "996", "995", "994", "993"],
            traces.Select(audit => audit.Fields!["fuelRemaining"]).ToArray());
    }

    [Fact]
    public async Task Unsupported_comparison_tree_keeps_the_generic_result_path()
    {
        using var host = SandboxTestHost.Create();
        var plan = await ScalarReturnTestSupport.PrepareAsync(
            host,
            ScalarReturnTestModules.Custom(
                "scalar-return-comparison-fallback",
                """[{ "name": "value", "type": "I64" }]""",
                "Bool",
                """{ "op": "lt", "left": { "var": "value" }, "right": { "i64": 10 } }"""));

        var result = await ScalarReturnTestSupport.ExecuteAsync(
            host,
            plan,
            SandboxValue.FromInt64(9));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.True(((BoolValue)result.Value!).Value);
        ScalarReturnTestSupport.AssertUsage(result.ResourceUsage, fuel: 5);
    }

    [Fact]
    public async Task Pure_intrinsic_call_keeps_generic_binding_accounting()
    {
        using var host = SandboxTestHost.Create();
        var plan = await ScalarReturnTestSupport.PrepareAsync(
            host,
            ScalarReturnTestModules.Expression(
                "scalar-return-math-call-fallback",
                "F64",
                """{ "call": "math.floor", "args": [{ "var": "value" }] }"""));

        var result = await ScalarReturnTestSupport.ExecuteAsync(
            host,
            plan,
            SandboxValue.FromDouble(3.75));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(3, ((F64Value)result.Value!).Value);
        ScalarReturnTestSupport.AssertUsage(result.ResourceUsage, fuel: 6, hostCalls: 1);
    }

    [Theory]
    [InlineData(false, 42L, 4L)]
    [InlineData(true, 43L, 6L)]
    public async Task Pending_binding_return_keeps_the_generic_async_continuation(
        bool nestedInArithmetic,
        long expected,
        long expectedFuel)
    {
        var invoked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<SandboxValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(PendingI64Binding(invoked, release));
            builder.UseInterpreter();
        });
        var expression = nestedInArithmetic
            ? """
              {
                "op": "add",
                "left": { "call": "test.pendingI64", "args": [] },
                "right": { "i64": 1 }
              }
              """
            : """{ "call": "test.pendingI64", "args": [] }""";
        var plan = await ScalarReturnTestSupport.PrepareAsync(
            host,
            ScalarReturnTestModules.NoArgumentExpression(
                $"scalar-return-async-{(nestedInArithmetic ? "tree" : "call")}-fallback",
                "I64",
                expression),
            allowRuntimeAsync: true);

        var execution = host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            ScalarReturnTestSupport.Options()).AsTask();
        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(execution.IsCompleted);
        release.SetResult(SandboxValue.FromInt64(42));
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expected, ((I64Value)result.Value!).Value);
        ScalarReturnTestSupport.AssertUsage(result.ResourceUsage, expectedFuel, hostCalls: 1);
    }

    private static BindingDescriptor PendingI64Binding(
        TaskCompletionSource<bool> invoked,
        TaskCompletionSource<SandboxValue> release)
        => new(
            "test.pendingI64",
            SemVersion.One,
            [],
            SandboxType.I64,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) =>
            {
                invoked.SetResult(true);
                return new ValueTask<SandboxValue>(release.Task);
            },
            CompiledBinding.RuntimeStub(
                typeof(CompiledRuntime).FullName!,
                nameof(CompiledRuntime.CallBinding)))
        {
            IsAsync = true
        };

    private static string Node(SandboxAuditEvent audit)
        => $"{audit.Fields!["category"]}:{audit.Fields["nodeKind"]}";
}
