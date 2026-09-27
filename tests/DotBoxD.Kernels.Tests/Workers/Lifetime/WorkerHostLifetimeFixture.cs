using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotBoxD.Hosting;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Workers;

internal static class WorkerHostLifetimeFixture
{
    internal static ExecutionPlan Prepare()
    {
        using var host = SandboxHost.Create();
        var module = host.ImportJsonAsync(SandboxTestHost.PureScoreJson()).GetAwaiter().GetResult();
        return host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .WithFuel(1_000).WithWallTime(TimeSpan.FromSeconds(10)).Build()).GetAwaiter().GetResult();
    }

    internal static void Execute(SandboxHostWorkerClient worker, ExecutionPlan plan)
    {
        var result = worker.ExecuteInWorkerAsync(plan, "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted }).GetAwaiter().GetResult();
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(35, Assert.IsType<I32Value>(result.Value).Value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static (SandboxHostWorkerClient Worker, WeakReference[] References) CreateDisposed(string state)
    {
        var plan = Prepare();
        var owner = new FactoryOwner();
        var worker = new SandboxHostWorkerClient(owner.Create);
        var references = new List<WeakReference> { new(owner) };
        if (state == "initialized")
        {
            Execute(worker, plan);
        }
        else if (state == "failed")
        {
            owner.Failure = new InvalidOperationException("local factory failure");
            var error = Assert.Throws<InvalidOperationException>(() => Execute(worker, plan));
            Assert.Same(owner.Failure, error);
            references.Add(new WeakReference(error));
        }

        worker.Dispose();
        Assert.Throws<ObjectDisposedException>(() => Execute(worker, plan));
        Assert.Equal(state == "unused" ? 0 : 1, owner.Calls);
        if (state == "initialized")
        {
            AssertHostDisposed(owner, plan);
            references.Add(ReleaseHostReference(owner));
        }
        return (worker, references.ToArray());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static (SandboxHostWorkerClient Worker, WeakReference Reference) CreateLive(bool initialized)
    {
        var owner = new FactoryOwner();
        var worker = new SandboxHostWorkerClient(owner.Create);
        if (initialized)
        {
            Execute(worker, Prepare());
        }
        return (worker, initialized ? ReleaseHostReference(owner) : new WeakReference(owner));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static WeakReference ReleaseHostReference(FactoryOwner owner)
    {
        var host = Assert.IsType<SandboxHost>(owner.Host);
        owner.Host = null;
        return new WeakReference(host);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void AssertHostDisposed(FactoryOwner owner, ExecutionPlan plan)
    {
        var host = Assert.IsType<SandboxHost>(owner.Host);
        Assert.Throws<ObjectDisposedException>(() => host.PrepareAsync(plan.Module, plan.Policy));
    }

    internal static async Task AssertCollectedAsync(WeakReference[] references)
    {
        var elapsed = Stopwatch.StartNew();
        do
        {
            Collect();
            if (references.All(reference => !reference.IsAlive))
            {
                return;
            }
            await Task.Delay(10);
        }
        while (elapsed.Elapsed < TimeSpan.FromSeconds(5));
        Assert.All(references, reference => Assert.False(reference.IsAlive,
            "A disposed client must release its factory, host, and cached factory failure."));
    }

    internal static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    internal sealed class FactoryOwner
    {
        private int _calls;
        internal int Calls => Volatile.Read(ref _calls);
        internal SandboxHost? Host { get; set; }
        internal InvalidOperationException? Failure { get; set; }
        internal ManualResetEventSlim? Entered { get; init; }
        internal ManualResetEventSlim? Release { get; init; }

        internal SandboxHost Create()
        {
            Interlocked.Increment(ref _calls);
            Entered?.Set();
            if (Release is { } release)
            {
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)), "Factory release was not signaled.");
            }
            if (Failure is { } failure)
            {
                throw failure;
            }
            var host = SandboxHost.Create(builder => builder.UseInterpreter());
            Host = host;
            return host;
        }
    }
}
