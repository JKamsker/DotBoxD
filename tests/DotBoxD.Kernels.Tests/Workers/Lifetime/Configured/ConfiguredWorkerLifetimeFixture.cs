using System.Runtime.CompilerServices;
using DotBoxD.Hosting;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Workers;

internal static class ConfiguredWorkerLifetimeFixture
{
    internal static SandboxHost Host(ISandboxWorkerClient worker)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    internal static Task<SandboxExecutionResult> Execute(SandboxHost host)
    {
        var module = host.ImportJsonAsync(SandboxTestHost.PureScoreJson()).GetAwaiter().GetResult();
        var plan = host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .WithFuel(1_000).WithWallTime(TimeSpan.FromSeconds(10)).Build()).GetAwaiter().GetResult();
        return host.ExecuteAsync(plan, "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Interpreted,
                Isolation = SandboxIsolation.WorkerProcess,
            }).AsTask();
    }

    internal static void AssertSuccess(SandboxExecutionResult result)
    {
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(35, Assert.IsType<I32Value>(result.Value).Value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Probe Create(int count, bool execute, bool dispose = false, bool keepHosts = true)
    {
        var hosts = new SandboxHost[count];
        var clients = new WeakReference[count];
        var counters = new Counter[count];
        var results = new List<Task<SandboxExecutionResult>>();
        for (var index = 0; index < count; index++)
        {
            var counter = new Counter();
            var worker = new Worker(counter);
            var host = Host(worker);
            hosts[index] = host;
            clients[index] = new WeakReference(worker);
            counters[index] = counter;
            if (execute)
            {
                var result = Execute(host);
                AssertSuccess(result.GetAwaiter().GetResult());
                results.Add(result);
            }
            if (dispose)
            {
                host.Dispose();
                host.Dispose();
            }
        }

        return new Probe(keepHosts ? hosts : [], clients, counters, results.ToArray());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static PendingProbe CreatePending()
    {
        var counter = new Counter();
        var gate = new WorkerGate();
        var worker = new Worker(counter, gate);
        var host = Host(worker);
        var execution = Execute(host);
        Assert.False(execution.IsCompleted);
        Assert.NotNull(gate.Result);
        return new PendingProbe(host, execution, new WeakReference(worker), gate, counter);
    }

    internal static async Task AssertCollectedAsync(WeakReference[] clients)
    {
        var deadline = Environment.TickCount64 + 5_000;
        do
        {
            WorkerHostLifetimeFixture.Collect();
            if (clients.All(client => !client.IsAlive))
            {
                return;
            }
            await Task.Delay(20);
        } while (Environment.TickCount64 < deadline);

        Assert.All(clients, client => Assert.False(client.IsAlive,
            "A disposed host must release its configured worker after active calls complete."));
    }

    internal sealed record Probe(
        SandboxHost[] Hosts, WeakReference[] Clients, Counter[] Counters, Task<SandboxExecutionResult>[] Results);

    internal sealed record PendingProbe(
        SandboxHost Host, Task<SandboxExecutionResult> Execution, WeakReference Client, WorkerGate Gate, Counter Counter);

    internal sealed class Counter
    {
        internal int Calls { get; set; }
        internal int Disposals { get; set; }
    }

    internal sealed class WorkerGate
    {
        internal TaskCompletionSource<SandboxExecutionResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal SandboxExecutionResult? Result { get; set; }
        internal CancellationToken CancellationToken { get; set; }

        internal void Complete(bool fail = false)
        {
            if (fail)
            {
                Completion.SetException(new InvalidOperationException("local worker failure"));
            }
            else
            {
                Completion.SetResult(Assert.IsType<SandboxExecutionResult>(Result));
            }
        }
    }

    internal sealed class Worker(Counter counter, WorkerGate? gate = null) : ISandboxWorkerClient, IDisposable
    {
        private readonly SandboxHostWorkerClient _inner = new(() => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        }));

        public ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
            ExecutionPlan plan, string entrypoint, SandboxValue input, SandboxExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            counter.Calls++;
            var result = _inner.ExecuteInWorkerAsync(plan, entrypoint, input, options, cancellationToken)
                .GetAwaiter().GetResult();
            if (gate is null)
            {
                return ValueTask.FromResult(result);
            }

            gate.Result = result;
            gate.CancellationToken = cancellationToken;
            return new ValueTask<SandboxExecutionResult>(gate.Completion.Task);
        }

        public void Dispose()
        {
            counter.Disposals++;
            _inner.Dispose();
        }
    }
}
