using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Kernels.Tests.Audit;

internal sealed class BlockedAuditPublication(
    SandboxHost host,
    Task<SandboxExecutionResult> execution,
    WeakReference observer,
    ManualResetEventSlim entered,
    ManualResetEventSlim release,
    List<SandboxAuditEvent> observed) : IAsyncDisposable
{
    public SandboxHost Host { get; } = host;
    public Task<SandboxExecutionResult> Execution { get; } = execution;
    public WeakReference Observer { get; } = observer;
    public ManualResetEventSlim Entered { get; } = entered;
    public ManualResetEventSlim Release { get; } = release;
    public List<SandboxAuditEvent> Observed { get; } = observed;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static BlockedAuditPublication Start()
    {
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var events = new List<SandboxAuditEvent>();
        var observer = new BlockingObserver(entered, release, events);
        // The task captures only the host, so it cannot retain observers after publication.
        var host = AuditObserverLifetimeFixture.Host(observer.OnEvent);
        var execution = Task.Run(() => AuditObserverLifetimeFixture.Execute(host));
        return new(host, execution, new WeakReference(observer), entered, release, events);
    }

    public async ValueTask DisposeAsync()
    {
        Release.Set();
        try
        {
            _ = await Execution.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            Host.Dispose();
            Entered.Dispose();
            Release.Dispose();
        }
    }

    private sealed class BlockingObserver(
        ManualResetEventSlim entered,
        ManualResetEventSlim release,
        List<SandboxAuditEvent> observed)
    {
        private bool _first = true;

        public void OnEvent(SandboxAuditEvent auditEvent)
        {
            observed.Add(auditEvent);
            if (_first)
            {
                _first = false;
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("The test did not release the audit callback.");
                }
            }
        }
    }
}
