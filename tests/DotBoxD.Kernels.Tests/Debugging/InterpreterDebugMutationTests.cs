using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Debugging;

public sealed class InterpreterDebugMutationTests
{
    [Fact]
    public async Task Frame_allows_validated_variable_replacement()
    {
        var hook = new ReplacingHook(frame =>
        {
            Assert.False(frame.TrySetVariable("value", SandboxValue.FromString("wrong"), out var typeError));
            Assert.Equal(SandboxErrorCode.InvalidInput, typeError!.Code);
            Assert.True(frame.TrySetVariable("value", SandboxValue.FromInt32(41), out var error), error?.SafeMessage);
        });
        var (host, plan) = await PrepareAsync(IncrementModule());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(1),
            new SandboxExecutionOptions { DebugHook = hook });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(42, Assert.IsType<I32Value>(result.Value).Value);
    }

    [Fact]
    public async Task Frame_replaces_existing_structured_members_without_introducing_values()
    {
        var hook = new ReplacingHook(frame =>
        {
            Assert.False(
                frame.TrySetMember(
                    "values",
                    [new SandboxDebugListIndex(5)],
                    SandboxValue.FromInt32(9),
                    out var rangeError));
            Assert.Equal(SandboxErrorCode.InvalidInput, rangeError!.Code);
            Assert.True(
                frame.TrySetMember(
                    "values",
                    [new SandboxDebugListIndex(0)],
                    SandboxValue.FromInt32(42),
                    out var error),
                error?.SafeMessage);
        });
        var (host, plan) = await PrepareAsync(ListReadModule());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1)], SandboxType.I32),
            new SandboxExecutionOptions { DebugHook = hook });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(42, Assert.IsType<I32Value>(result.Value).Value);
    }

    [Fact]
    public async Task Frame_rejects_values_outside_the_original_resource_limits()
    {
        SandboxError? writeError = null;
        var hook = new ReplacingHook(frame =>
            Assert.False(
                frame.TrySetVariable("value", SandboxValue.FromString("too-long"), out writeError)));
        var (host, plan) = await PrepareAsync(StringIdentityModule(), maxStringLength: 3);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromString("ok"),
            new SandboxExecutionOptions { DebugHook = hook });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, writeError!.Code);
        Assert.Equal("ok", Assert.IsType<StringValue>(result.Value).Value);
    }

    private static async Task<(DotBoxD.Hosting.Execution.SandboxHost Host, ExecutionPlan Plan)> PrepareAsync(
        SandboxModule module,
        int? maxStringLength = null)
    {
        var host = SandboxTestHost.Create();
        var policyBuilder = SandboxPolicyBuilder.Create().WithFuel(1_000).WithWallTime(TimeSpan.FromSeconds(2));
        if (maxStringLength is not null)
        {
            policyBuilder.WithMaxStringLength(maxStringLength.Value);
        }

        var policy = policyBuilder.Build();
        return (host, await host.PrepareAsync(module, policy));
    }

    private static SandboxModule IncrementModule()
    {
        var span = new SourceSpan(1, 1);
        return Module(new SandboxFunction(
            "main",
            true,
            [new Parameter("value", SandboxType.I32)],
            SandboxType.I32,
            [new ReturnStatement(
                new BinaryExpression(
                    new VariableExpression("value", span),
                    "+",
                    new LiteralExpression(SandboxValue.FromInt32(1), span),
                    span),
                span)]));
    }

    private static SandboxModule ListReadModule()
    {
        var span = new SourceSpan(1, 1);
        return Module(new SandboxFunction(
            "main",
            true,
            [new Parameter("values", SandboxType.List(SandboxType.I32))],
            SandboxType.I32,
            [new ReturnStatement(
                new CallExpression(
                    "list.get",
                    [
                        new VariableExpression("values", span),
                        new LiteralExpression(SandboxValue.FromInt32(0), span)
                    ],
                    null,
                    span),
                span)]));
    }

    private static SandboxModule StringIdentityModule()
    {
        var span = new SourceSpan(1, 1);
        return Module(new SandboxFunction(
            "main",
            true,
            [new Parameter("value", SandboxType.String)],
            SandboxType.String,
            [new ReturnStatement(new VariableExpression("value", span), span)]));
    }

    private static SandboxModule Module(SandboxFunction function)
        => new(
            "debug-writes",
            new SemVersion(1, 0, 0),
            new SemVersion(1, 0, 0),
            [],
            [function],
            new Dictionary<string, string>());

    private sealed class ReplacingHook(Action<ISandboxDebugFrame> replace) : ISandboxExecutionDebugHook
    {
        private bool _replaced;

        public ValueTask OnCheckpointAsync(SandboxDebugCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            if (!_replaced && checkpoint.Kind == SandboxDebugCheckpointKind.FunctionEntry)
            {
                _replaced = true;
                replace(checkpoint.Frame);
            }

            return default;
        }
    }
}
