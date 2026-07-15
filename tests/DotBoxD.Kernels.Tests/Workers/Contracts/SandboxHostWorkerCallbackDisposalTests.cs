using DotBoxD.Hosting;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class SandboxHostWorkerCallbackDisposalTests
{
    [Fact]
    public async Task Worker_callback_disposing_host_fails_before_success_publication()
    {
        var observed = new List<SandboxAuditEvent>();
        SandboxHost? host = null;
        using var worker = new ReentrantDisposingWorker(() => host!.Dispose());
        host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
            builder.ForwardAuditEventsTo(observed.Add);
        });
        var plan = await PrepareAsync(host);
        SandboxExecutionResult? result = null;

        var exception = await Record.ExceptionAsync(async () =>
        {
            result = await host.ExecuteAsync(
                plan,
                "main",
                Input(),
                new SandboxExecutionOptions
                {
                    Mode = ExecutionMode.Interpreted,
                    Isolation = SandboxIsolation.WorkerProcess
                });
        });

        Assert.IsType<ObjectDisposedException>(exception);
        Assert.Null(result);
        Assert.Empty(observed);
        Assert.Equal(1, worker.Calls);
    }

    private static async ValueTask<ExecutionPlan> PrepareAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static SandboxValue Input()
        => SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);

    private static SandboxHost WorkerHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });

    private sealed class ReentrantDisposingWorker(Action disposeOwner) : ISandboxWorkerClient, IDisposable
    {
        private readonly SandboxHostWorkerClient _inner = new(WorkerHost);

        public int Calls { get; private set; }

        public async ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            var result = await _inner.ExecuteInWorkerAsync(
                    plan,
                    entrypoint,
                    input,
                    options,
                    cancellationToken)
                .ConfigureAwait(false);
            disposeOwner();
            return result;
        }

        public void Dispose()
            => _inner.Dispose();
    }
}
