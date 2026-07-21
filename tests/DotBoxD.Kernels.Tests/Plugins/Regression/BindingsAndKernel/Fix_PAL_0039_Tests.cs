using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Plugins.Regression.BindingsAndKernel;

/// <summary>
/// Regression coverage for PAL-0039: binding dispatch creates a wall-time
/// cancellation source per host call. Both interpreted and compiled binding
/// dispatch call <see cref="SandboxContext.CreateWallTimeToken"/> for every
/// invocation. When the run cancellation token is the default
/// (non-cancelable) token, the wall-time deadline is already tracked by the
/// <see cref="ResourceMeter"/>, so the fast path should not allocate a fresh
/// linked <see cref="CancellationTokenSource"/> (plus its armed timer state)
/// per call.
/// </summary>
public sealed class Fix_PAL_0039_Tests
{
    // A generous wall-time budget so the produced token is never cancelled
    // mid-test and the deadline is the only reason a source would exist.
    private static SandboxContext Context(CancellationToken cancellationToken = default)
    {
        var limits = new ResourceLimits(
            MaxFuel: 1_000_000,
            MaxAllocatedBytes: 1_000_000,
            MaxWallTime: TimeSpan.FromHours(1));

        return new SandboxContext(
            SandboxRunId.New(),
            SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits },
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            cancellationToken);
    }

    [Fact]
    public void Wall_time_token_for_non_cancelable_run_does_not_allocate_per_call()
    {
        var context = Context();

        // Warm up first-call JIT / one-time allocations so the measured loop
        // only reflects per-call dispatch cost.
        DisposeIfOwned(context.CreateWallTimeToken());

        const int iterations = 1_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            DisposeIfOwned(context.CreateWallTimeToken());
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // RED until PAL-0039 is fixed: today every call links a fresh
        // CancellationTokenSource and arms a CancelAfter timer, so the fast
        // path allocates hundreds of bytes per invocation. A non-cancelable
        // run with the deadline tracked by the ResourceMeter must not allocate
        // a per-call cancellation source.
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Wall_time_token_for_non_cancelable_run_reuses_a_shared_source()
    {
        var context = Context();

        var first = context.CreateWallTimeToken();
        var second = context.CreateWallTimeToken();

        try
        {
            // RED until PAL-0039 is fixed: CreateLinkedTokenSource currently
            // returns a brand-new instance every call. On the fast path (a
            // non-cancelable run token) there is no asynchronous cancellation
            // to link, so the same wall-time source should be reused across
            // calls instead of allocating a distinct one each time.
            Assert.Same(first, second);
        }
        finally
        {
            DisposeIfOwned(first);
            if (!ReferenceEquals(first, second))
            {
                DisposeIfOwned(second);
            }
        }
    }

    [Fact]
    public void Binding_wall_time_lease_for_cancelable_run_does_not_allocate_per_call()
    {
        using var runCancellation = new CancellationTokenSource();
        var context = Context(runCancellation.Token);
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
        var context = Context(runCancellation.Token);
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
        var context = Context(runCancellation.Token);
        var lease = context.CreateBindingWallTimeToken();
        var deadlineToken = lease.Token;

        lease.Dispose();
        await runCancellation.CancelAsync();

        Assert.False(deadlineToken.IsCancellationRequested);
    }

    [Fact]
    public void Recycled_context_generation_replaces_its_wall_time_source()
    {
        var context = Context();
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
        var context = Context();
        var tokens = new CancellationToken[32];

        Parallel.For(0, tokens.Length, i =>
        {
            using var lease = context.CreateBindingWallTimeToken();
            tokens[i] = lease.Token;
        });

        Assert.All(tokens, token => Assert.Equal(tokens[0], token));
    }

    private static void DisposeIfOwned(CancellationTokenSource? source) => source?.Dispose();
}
