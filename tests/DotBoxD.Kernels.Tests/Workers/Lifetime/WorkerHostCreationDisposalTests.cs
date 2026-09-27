using System.Runtime.CompilerServices;
using DotBoxD.Hosting;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerHostCreationDisposalTests
{
    [Fact]
    public async Task Disposal_during_creation_releases_the_late_disposed_host()
    {
        var probe = await DisposeDuringCreationAsync();
        using var worker = probe.Worker;
        await WorkerHostLifetimeFixture.AssertCollectedAsync([probe.Host]);
        GC.KeepAlive(worker);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(SandboxHostWorkerClient Worker, WeakReference Host)> DisposeDuringCreationAsync()
    {
        var plan = WorkerHostLifetimeFixture.Prepare();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var owner = new WorkerHostLifetimeFixture.FactoryOwner { Entered = entered, Release = release };
        var worker = new SandboxHostWorkerClient(owner.Create);
        var execution = Task.Run(() => WorkerHostLifetimeFixture.Execute(worker, plan));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "Worker factory was not entered.");
            worker.Dispose();
            release.Set();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => execution.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, owner.Calls);
            WorkerHostLifetimeFixture.AssertHostDisposed(owner, plan);
            return (worker, WorkerHostLifetimeFixture.ReleaseHostReference(owner));
        }
        finally
        {
            release.Set();
            worker.Dispose();
        }
    }
}
