using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Audit;

internal static class AuditObserverLifetimeFixture
{
    public static SandboxHost Host(params Action<SandboxAuditEvent>[] observers)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            foreach (var observer in observers)
            {
                builder.ForwardAuditEventsTo(observer);
            }
        });

    public static SandboxExecutionResult Execute(SandboxHost host)
    {
        var module = host.ImportJsonAsync(SandboxTestHost.PureScoreJson()).AsTask().GetAwaiter().GetResult();
        var policy = SandboxPolicyBuilder.Create().WithFuel(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10)).Build();
        var plan = host.PrepareAsync(module, policy).AsTask().GetAwaiter().GetResult();
        return host.ExecuteAsync(plan, "main", SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
                new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess })
            .AsTask().GetAwaiter().GetResult();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (SandboxHost? Host, WeakReference[] References) Create(
        int count, bool execute = false, bool dispose = false, bool keepHost = true)
    {
        var observers = Enumerable.Range(0, count).Select(_ => new Observer()).ToArray();
        var references = observers.Select(observer => new WeakReference(observer)).ToArray();
        var host = Host(observers.Select(observer => (Action<SandboxAuditEvent>)observer.OnEvent).ToArray());
        if (execute)
        {
            _ = Execute(host);
        }
        if (dispose)
        {
            host.Dispose();
        }
        return (keepHost ? host : null, references);
    }

    public static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public static async Task AssertCollectedAsync(params WeakReference[] references)
    {
        var deadline = Environment.TickCount64 + 5_000;
        do
        {
            Collect();
            if (references.All(reference => !reference.IsAlive))
            {
                return;
            }
            await Task.Delay(20);
        } while (Environment.TickCount64 < deadline);

        Assert.All(references, reference => Assert.False(reference.IsAlive));
    }

    internal sealed class Observer : IDisposable
    {
        public List<SandboxAuditEvent> Events { get; } = [];
        public int DisposeCalls { get; private set; }
        public void OnEvent(SandboxAuditEvent auditEvent) => Events.Add(auditEvent);
        public void Dispose() => DisposeCalls++;
    }
}
