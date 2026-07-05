using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Runtime.TimeRandom;

public sealed class TimeAndRandomCancellationRegressionTests
{
    [Fact]
    public async Task Random_descriptor_honors_pre_canceled_token_before_audit_and_state_advance()
    {
        var audit = new InMemoryAuditSink();
        var context = CreateContext(
            SandboxPolicyBuilder.Create()
                .GrantRandom()
                .Deterministic(DateTimeOffset.UnixEpoch, randomSeed: 123)
                .Build(),
            audit);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var ex = await Record.ExceptionAsync(async () =>
            await SafeRandomBindings.NextI32.Invoke(
                context,
                [SandboxValue.FromInt32(0), SandboxValue.FromInt32(1000)],
                cancellation.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.DoesNotContain(audit.Events, e => e.Kind == "BindingCall" && e.BindingId == "random.nextI32");
        var firstUncanceled = await SafeRandomBindings.NextI32.Invoke(
            context,
            [SandboxValue.FromInt32(0), SandboxValue.FromInt32(1000)],
            CancellationToken.None);
        Assert.Equal(692, ((I32Value)firstUncanceled).Value);
    }

    [Fact]
    public async Task Time_descriptor_honors_pre_canceled_token_before_audit()
    {
        var audit = new InMemoryAuditSink();
        var context = CreateContext(
            SandboxPolicyBuilder.Create()
                .GrantTimeNow()
                .Deterministic(DateTimeOffset.UnixEpoch, randomSeed: 123)
                .Build(),
            audit);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var ex = await Record.ExceptionAsync(async () =>
            await SafeTimeBindings.NowUnixMillis.Invoke(context, [], cancellation.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.DoesNotContain(audit.Events, e => e.Kind == "BindingCall" && e.BindingId == "time.nowUnixMillis");
    }

    private static SandboxContext CreateContext(SandboxPolicy policy, InMemoryAuditSink audit)
        => new(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistry([]),
            audit,
            CancellationToken.None);
}
