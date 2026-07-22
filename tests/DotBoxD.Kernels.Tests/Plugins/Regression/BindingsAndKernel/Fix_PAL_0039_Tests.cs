using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Plugins.Regression.BindingsAndKernel;

/// <summary>
/// Regression coverage for PAL-0039: interpreted and compiled binding dispatch share one internally
/// owned wall-time source per execution while public token requests remain caller-owned.
/// </summary>
public sealed class Fix_PAL_0039_Tests
{
    private static SandboxContext Context(
        CancellationToken cancellationToken = default,
        BindingRegistry? bindings = null,
        TimeSpan? wallTime = null)
    {
        var limits = new ResourceLimits(
            MaxFuel: 1_000_000,
            MaxAllocatedBytes: 1_000_000,
            MaxWallTime: wallTime ?? TimeSpan.FromHours(1));

        return new SandboxContext(
            SandboxRunId.New(),
            SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits },
            new ResourceMeter(limits),
            bindings ?? new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            cancellationToken);
    }

    [Fact]
    public void Public_wall_time_tokens_are_distinct_and_caller_owned()
    {
        using var context = Context();
        using var first = context.CreateWallTimeToken();
        using var second = context.CreateWallTimeToken();

        Assert.NotSame(first, second);
        first.Dispose();
        Assert.Throws<ObjectDisposedException>(() => first.CancelAfter(TimeSpan.FromSeconds(1)));
        Assert.False(second.IsCancellationRequested);
    }

    [Fact]
    public void Public_token_cancellation_does_not_poison_internal_binding_deadline()
    {
        using var context = Context();
        using var publicSource = context.CreateWallTimeToken();
        using var lease = context.CreateBindingWallTimeToken();

        publicSource.Cancel();

        Assert.True(publicSource.IsCancellationRequested);
        Assert.False(lease.IsCancellationRequested);
        Assert.False(lease.Token.IsCancellationRequested);
    }

    [Fact]
    public void Public_wall_time_token_preserves_pre_canceled_context_state()
    {
        using var runCancellation = new CancellationTokenSource();
        runCancellation.Cancel();
        using var context = Context(runCancellation.Token);

        using var publicSource = context.CreateWallTimeToken();

        Assert.True(publicSource.IsCancellationRequested);
    }

    [Fact]
    public void Binding_wall_time_lease_for_cancelable_run_does_not_allocate_per_call()
    {
        using var runCancellation = new CancellationTokenSource();
        using var context = Context(runCancellation.Token);
        context.CreateBindingWallTimeToken().Dispose();

        const int iterations = 1_000;
        var allTokensCancelable = true;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            using var lease = context.CreateBindingWallTimeToken();
            allTokensCancelable &= lease.Token.CanBeCanceled;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allTokensCancelable);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public async Task Disposing_nested_lease_does_not_unlink_outer_run_cancellation()
    {
        using var runCancellation = new CancellationTokenSource();
        using var context = Context(runCancellation.Token);
        using var outer = context.CreateBindingWallTimeToken();
        var inner = context.CreateBindingWallTimeToken();

        inner.Dispose();
        await runCancellation.CancelAsync();

        Assert.True(outer.IsCancellationRequested);
        Assert.True(outer.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task Disposed_final_lease_unlinks_run_cancellation_from_deadline_source()
    {
        using var runCancellation = new CancellationTokenSource();
        using var context = Context(runCancellation.Token);
        var lease = context.CreateBindingWallTimeToken();
        var deadlineToken = lease.Token;

        lease.Dispose();
        await runCancellation.CancelAsync();

        Assert.False(deadlineToken.IsCancellationRequested);
    }

    [Fact]
    public void Recycled_context_generation_replaces_its_wall_time_source()
    {
        using var context = Context();
        var first = context.CreateBindingWallTimeToken();
        var firstToken = first.Token;
        first.Dispose();

        context.Budget.ResetForReuse();
        context.ResetForCompiledNoAuditReuse();

        using var second = context.CreateBindingWallTimeToken();
        Assert.NotEqual(firstToken, second.Token);
    }

    [Fact]
    public void Concurrent_first_leases_publish_one_wall_time_source()
    {
        using var context = Context();
        var tokens = new CancellationToken[32];

        Parallel.For(0, tokens.Length, i =>
        {
            using var lease = context.CreateBindingWallTimeToken();
            tokens[i] = lease.Token;
        });

        Assert.All(tokens, token => Assert.Equal(tokens[0], token));
    }

    [Fact]
    public void Expired_shared_deadline_rejects_before_invoking_token_ignoring_binding()
    {
        var invocationCount = 0;
        var descriptor = new BindingDescriptor(
            "test.ignore",
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) =>
            {
                invocationCount++;
                return ValueTask.FromResult(SandboxValue.Unit);
            },
            CompiledBinding.RuntimeStub(
                typeof(CompiledRuntime).FullName!,
                nameof(CompiledRuntime.CallBinding)));
        var bindings = new BindingRegistryBuilder().Add(descriptor).Build();
        using var context = Context(bindings: bindings, wallTime: TimeSpan.FromMilliseconds(25));
        using (var lease = context.CreateBindingWallTimeToken())
        {
            Assert.True(lease.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)));
        }

        // CancelAfter is an advisory wake-up and may fire at the platform timer boundary just
        // before the Stopwatch-based resource deadline. Prove that the exact deadline has elapsed
        // before asserting the dispatcher's pre-invocation rejection.
        Assert.True(SpinWait.SpinUntil(() =>
        {
            try
            {
                context.Budget.CheckDeadline();
                return false;
            }
            catch (SandboxRuntimeException exception)
                when (exception.Error.Code == SandboxErrorCode.Timeout)
            {
                return true;
            }
        }, TimeSpan.FromSeconds(5)));

        var exception = Assert.Throws<SandboxRuntimeException>(
            () => CompiledRuntime.CallBinding(context, descriptor.Id, []));

        Assert.Equal(SandboxErrorCode.Timeout, exception.Error.Code);
        Assert.Equal(0, invocationCount);
    }
}
