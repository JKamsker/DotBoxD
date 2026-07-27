using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.LocalCalls.InlineI32;

public sealed class InlineI32LocalFunctionFallbackTests
{
    public static TheoryData<string, int, int, long> UnsupportedShapes => new()
    {
        {
            InlineI32LocalFunctionModules.SingleCall(
                "inline-i32-repeated-parameter",
                """{ "op": "add", "left": { "var": "operand" }, "right": { "var": "operand" } }"""),
            21,
            42,
            11
        },
        {
            InlineI32LocalFunctionModules.SingleCall(
                "inline-i32-unused-parameter",
                """{ "i32": 42 }"""),
            7,
            42,
            9
        },
        {
            InlineI32LocalFunctionModules.SingleCall(
                "inline-i32-complex-argument",
                """{ "var": "operand" }""",
                """{ "op": "add", "left": { "var": "value" }, "right": { "i32": 1 } }"""),
            41,
            42,
            11
        },
        { InlineI32LocalFunctionModules.MultiStatementHelper("inline-i32-multi-statement"), 41, 42, 13 },
        { InlineI32LocalFunctionModules.TwoArgumentHelper("inline-i32-two-arguments"), 41, 42, 12 }
    };

    [Theory]
    [MemberData(nameof(UnsupportedShapes))]
    public async Task Unsupported_shapes_keep_generic_values_and_metering(
        string moduleJson,
        int input,
        int expected,
        long expectedFuel)
    {
        using var host = SandboxTestHost.Create();
        var plan = await InlineI32LocalFunctionTestSupport.PrepareAsync(host, moduleJson);

        var result = await InlineI32LocalFunctionTestSupport.ExecuteAsync(plan, input);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expected, InlineI32LocalFunctionTestSupport.ReadInt32(result));
        InlineI32LocalFunctionTestSupport.AssertUsage(result.ResourceUsage, expectedFuel);
    }

    [Fact]
    public async Task Debug_mode_keeps_exact_generic_trace_and_fuel_order()
    {
        using var host = SandboxTestHost.Create();
        var plan = await InlineI32LocalFunctionTestSupport.PrepareAsync(
            host,
            InlineI32LocalFunctionModules.SingleCall(
                "inline-i32-debug-fallback",
                """{ "op": "add", "left": { "var": "operand" }, "right": { "i32": 1 } }"""));

        var result = await InlineI32LocalFunctionTestSupport.ExecuteAsync(plan, input: 41, debug: true);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(42, InlineI32LocalFunctionTestSupport.ReadInt32(result));
        InlineI32LocalFunctionTestSupport.AssertUsage(result.ResourceUsage, fuel: 11);
        var traces = result.AuditEvents.Where(audit => audit.Kind == "DebugTrace").ToArray();
        Assert.Equal(
            [
                "statement:AssignmentStatement",
                "expression:CallExpression",
                "expression:VariableExpression",
                "statement:ReturnStatement",
                "expression:BinaryExpression",
                "expression:VariableExpression",
                "expression:LiteralExpression",
                "statement:ReturnStatement",
                "expression:VariableExpression"
            ],
            traces.Select(InlineI32LocalFunctionTestSupport.TraceNode).ToArray());
        Assert.Equal(
            ["998", "997", "996", "994", "993", "992", "991", "990", "989"],
            traces.Select(audit => audit.Fields!["fuelRemaining"]).ToArray());
    }

    [Theory]
    [InlineData(true, "local 'source' read before assignment", 4L)]
    [InlineData(false, "unknown local 'source' at runtime", 3L)]
    public async Task Unassigned_and_unknown_arguments_fall_back_to_named_runtime_errors(
        bool reserveSlot,
        string expectedMessage,
        long expectedFuel)
    {
        using var host = SandboxTestHost.Create();
        var prepared = await InlineI32LocalFunctionTestSupport.PrepareAsync(
            host,
            InlineI32LocalFunctionModules.SourceRead(
                "inline-i32-assigned-control",
                assignBeforeCall: true,
                reserveSlot: true));
        var invalid = await host.ImportJsonAsync(
            InlineI32LocalFunctionModules.SourceRead(
                "inline-i32-invalid-source",
                assignBeforeCall: false,
                reserveSlot));
        var tampered = InlineI32LocalFunctionTestSupport.ReplaceModule(prepared, invalid);

        var result = await InlineI32LocalFunctionTestSupport.ExecuteAsync(tampered, input: 41);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.Equal(expectedMessage, result.Error.SafeMessage);
        InlineI32LocalFunctionTestSupport.AssertUsage(result.ResourceUsage, expectedFuel);
    }

    [Fact]
    public async Task Unknown_helper_name_falls_back_after_evaluating_its_argument()
    {
        using var host = SandboxTestHost.Create();
        var prepared = await InlineI32LocalFunctionTestSupport.PrepareAsync(
            host,
            InlineI32LocalFunctionModules.SingleCall(
                "inline-i32-known-helper",
                """{ "var": "operand" }"""));
        var invalid = await host.ImportJsonAsync(
            InlineI32LocalFunctionModules.SingleCall(
                    "inline-i32-unknown-helper",
                    """{ "var": "operand" }""")
                .Replace("\"call\": \"step\"", "\"call\": \"missing\"", StringComparison.Ordinal));
        var tampered = InlineI32LocalFunctionTestSupport.ReplaceModule(prepared, invalid);

        var result = await InlineI32LocalFunctionTestSupport.ExecuteAsync(tampered, input: 41);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.Equal("unknown call 'missing' at runtime", result.Error.SafeMessage);
        InlineI32LocalFunctionTestSupport.AssertUsage(result.ResourceUsage, fuel: 4);
    }

    [Fact]
    public async Task Precancelled_debug_fallback_stops_before_fuel_or_trace()
    {
        using var host = SandboxTestHost.Create();
        var plan = await InlineI32LocalFunctionTestSupport.PrepareAsync(
            host,
            InlineI32LocalFunctionModules.SingleCall(
                "inline-i32-debug-cancellation",
                """{ "op": "add", "left": { "var": "operand" }, "right": { "i32": 1 } }"""));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await InlineI32LocalFunctionTestSupport.ExecuteAsync(
            plan,
            input: 41,
            debug: true,
            cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        InlineI32LocalFunctionTestSupport.AssertUsage(result.ResourceUsage, fuel: 0);
        Assert.DoesNotContain(result.AuditEvents, audit => audit.Kind == "DebugTrace");
    }

    [Fact]
    public async Task Collection_intrinsic_keeps_precedence_over_a_colliding_local_function()
    {
        using var host = SandboxTestHost.Create();
        var plan = await InlineI32LocalFunctionTestSupport.PrepareAsync(
            host,
            InlineI32LocalFunctionModules.CollectionPrecedence);
        var result = await new SandboxInterpreter().ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList(
                [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)],
                SandboxType.I32),
            InlineI32LocalFunctionTestSupport.Options(),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(2, InlineI32LocalFunctionTestSupport.ReadInt32(result));
        Assert.Equal(10, result.ResourceUsage.FuelUsed);
    }

    [Fact]
    public async Task Pending_local_helper_fallback_keeps_shared_assignment_continuation()
    {
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<SandboxValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = DotBoxD.Hosting.Execution.SandboxHost.Create(builder =>
        {
            builder.AddBinding(PendingBinding(invoked, release));
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync(InlineI32LocalFunctionModules.PendingHelper);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .AllowRuntimeAsync()
                .Build());
        var execution = new SandboxInterpreter().ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(41),
            InlineI32LocalFunctionTestSupport.Options(),
            CancellationToken.None).AsTask();

        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(execution.IsCompleted);
        release.SetResult(SandboxValue.FromInt32(42));
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(42, InlineI32LocalFunctionTestSupport.ReadInt32(result));
        Assert.Equal(11, result.ResourceUsage.FuelUsed);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
    }

    private static BindingDescriptor PendingBinding(
        TaskCompletionSource invoked,
        TaskCompletionSource<SandboxValue> release)
        => new(
            "test.pause",
            SemVersion.One,
            [SandboxType.I32],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) =>
            {
                invoked.SetResult();
                return new ValueTask<SandboxValue>(release.Task);
            },
            CompiledBinding.RuntimeStub(
                typeof(CompiledRuntime).FullName!,
                nameof(CompiledRuntime.CallBinding)))
        {
            IsAsync = true
        };
}
