using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerObserverContextLifetimeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Canceled_waiter_releases_ambient_state_while_worker_remains_pending(
        bool timeout, bool customSource)
    {
        using var fixture = new WorkerCancellationFixture(timeout, customSource);
        using var cancellation = new CancellationTokenSource();
        (Task<SandboxExecutionResult> Waiter, WorkerPendingCompletion Completion) request = (null!, null!);
        var reference = AmbientContextLifetime.Capture(() => request = fixture.Start(cancellation.Token));
        try
        {
            AmbientContextLifetime.AssertRetained(reference);
            if (!timeout)
            {
                cancellation.Cancel();
            }
            var result = await request.Waiter.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(timeout ? SandboxErrorCode.Timeout : SandboxErrorCode.Cancelled, result.Error!.Code);
            Assert.False(request.Completion.IsCompleted);

            await AmbientContextLifetime.AssertCollectedAsync(reference);
            Assert.False(request.Completion.IsCompleted);
            GC.KeepAlive(request);
        }
        finally
        {
            request.Completion.Succeed();
        }
    }
}
