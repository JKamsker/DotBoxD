using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Tests.Compiled.Core.ReturnValidation.Proof;

public sealed class FlatScalarListReturnValidationProofTests
{
    [Fact]
    public void Compiled_return_helper_preserves_flat_list_identity_and_resource_usage()
    {
        using var context = CreateContext();
        var value = I32List(256);
        var expectedType = SandboxType.List(SandboxType.I32);
        var usageBefore = context.Budget.Snapshot();

        var returned = CompiledRuntime.RequireValueTypeAndRecordValidation(
            context,
            value,
            expectedType);

        Assert.Same(value, returned);
        Assert.Equal(usageBefore, context.Budget.Snapshot());
        Assert.True(context.TryConsumeCompiledReturnValidation(value, expectedType));
    }

    private static ListValue I32List(int count)
    {
        var values = new SandboxValue[count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = SandboxValue.FromInt32(i);
        }

        return new ListValue(values, SandboxType.I32);
    }

    private static SandboxContext CreateContext()
    {
        var policy = SandboxPolicyBuilder.Create().Build();
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistry([]),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }
}
