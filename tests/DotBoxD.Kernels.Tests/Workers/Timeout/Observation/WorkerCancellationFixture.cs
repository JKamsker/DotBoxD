using System.Runtime.CompilerServices;
using DotBoxD.Hosting;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Workers;

internal sealed class WorkerCancellationFixture : IDisposable
{
    private readonly ExecutionPlan _plan;
    private readonly PendingWorker _worker;
    private readonly bool _timeout;

    internal WorkerCancellationFixture(bool timeout, bool customSource)
    {
        _timeout = timeout;
        _worker = new PendingWorker(customSource);
        Host = ConfiguredWorkerLifetimeFixture.Host(_worker);
        var module = Host.ImportJsonAsync(SandboxTestHost.PureScoreJson()).GetAwaiter().GetResult();
        _plan = Host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000)
            .WithWallTime(TimeSpan.FromSeconds(timeout ? 1 : 10)).Build()).GetAwaiter().GetResult();
    }

    internal SandboxHost Host { get; }

    internal (Task<SandboxExecutionResult> Waiter, WorkerPendingCompletion Completion) Start(CancellationToken token)
    {
        var waiter = Host.ExecuteAsync(_plan, "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Interpreted,
                Isolation = SandboxIsolation.WorkerProcess,
            }, token).AsTask();
        return (waiter, Assert.IsType<WorkerPendingCompletion>(_worker.Current));
    }

    private void StopWaiting(
        Task<SandboxExecutionResult> waiter, WorkerPendingCompletion completion, CancellationTokenSource cancellation)
    {
        if (!_timeout)
        {
            cancellation.Cancel();
        }
        var result = waiter.GetAwaiter().GetResult();
        Assert.False(result.Succeeded);
        Assert.Equal(_timeout ? SandboxErrorCode.Timeout : SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.False(completion.IsCompleted);
        Assert.True(completion.CancellationToken.IsCancellationRequested);
        Assert.Equal(1, _worker.Calls);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Probe AbandonFailure(bool timeout, bool disposeBeforeFailure, bool customSource, string marker)
    {
        using var fixture = new WorkerCancellationFixture(timeout, customSource);
        using var cancellation = new CancellationTokenSource();
        var request = fixture.Start(cancellation.Token);
        fixture.StopWaiting(request.Waiter, request.Completion, cancellation);
        if (disposeBeforeFailure)
        {
            fixture.Host.Dispose();
        }

        var error = new InvalidOperationException(marker);
        request.Completion.Fail(error);
        return new Probe(new WeakReference(error), request.Completion.Counter, customSource ? 1 : 0);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Probe CompleteLate(bool timeout, bool customSource, bool cancel, string marker)
    {
        using var fixture = new WorkerCancellationFixture(timeout, customSource);
        using var cancellation = new CancellationTokenSource();
        var request = fixture.Start(cancellation.Token);
        fixture.StopWaiting(request.Waiter, request.Completion, cancellation);
        object completed;
        if (cancel)
        {
            var error = new OperationCanceledException(marker, new CancellationToken(canceled: true));
            request.Completion.Fail(error);
            completed = error;
        }
        else
        {
            request.Completion.Succeed();
            completed = request.Completion.Result;
        }
        return new Probe(new WeakReference(completed), request.Completion.Counter, customSource ? 1 : 0);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Probe Reuse(bool timeout, bool fail, string marker)
    {
        using var fixture = new WorkerCancellationFixture(timeout, customSource: true);
        using var cancellation = new CancellationTokenSource();
        var first = fixture.Start(cancellation.Token);
        fixture.StopWaiting(first.Waiter, first.Completion, cancellation);
        var second = fixture.Start(CancellationToken.None);
        second.Completion.Succeed();
        ConfiguredWorkerLifetimeFixture.AssertSuccess(second.Waiter.GetAwaiter().GetResult());
        Assert.Equal(2, fixture._worker.Calls);
        Assert.Equal(1, second.Completion.Counter.Calls);
        Assert.False(first.Completion.IsCompleted);
        object completed;
        if (fail)
        {
            var error = new InvalidOperationException(marker);
            first.Completion.Fail(error);
            completed = error;
        }
        else
        {
            first.Completion.Succeed();
            completed = first.Completion.Result;
        }
        return new Probe(new WeakReference(completed), first.Completion.Counter, 1);
    }

    public void Dispose()
    {
        Host.Dispose();
        _worker.Dispose();
    }

    internal sealed record Probe(
        WeakReference Completed, WorkerPendingCompletion.ConsumptionCounter Counter, int ExpectedConsumptions);

    private sealed class PendingWorker(bool customSource) : ISandboxWorkerClient, IDisposable
    {
        private readonly SandboxHostWorkerClient _inner = new(() => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        }));

        internal WorkerPendingCompletion? Current { get; private set; }
        internal int Calls { get; private set; }

        public ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
            ExecutionPlan plan, string entrypoint, SandboxValue input, SandboxExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            var result = _inner.ExecuteInWorkerAsync(plan, entrypoint, input, options, cancellationToken)
                .GetAwaiter().GetResult();
            ConfiguredWorkerLifetimeFixture.AssertSuccess(result);
            Current = new WorkerPendingCompletion(result, cancellationToken, customSource);
            return Current.AsValueTask();
        }

        public void Dispose()
        {
            Current = null;
            _inner.Dispose();
        }
    }
}
