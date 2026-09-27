using System.Net;
using System.Runtime.CompilerServices;
using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

internal sealed class DnsFailureObservationFixture : IDisposable
{
    private readonly SafeInMemoryHttpMessageInvoker _invoker = new("ok");
    private readonly SandboxPolicy _policy;
    private readonly CancellationTokenSource _callerCancellation = new();
    private readonly CancellationTokenSource _contextCancellation = new();
    private PendingDnsResolution _resolution;

    private DnsFailureObservationFixture(string mode, bool customSource)
    {
        _policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024,
                timeout: TimeSpan.FromSeconds(mode == "timeout" ? 1 : 10))
            .WithFuel(5_000).WithWallTime(TimeSpan.FromSeconds(10)).Build();
        _resolution = new PendingDnsResolution(customSource);
        Audit = new InMemoryAuditSink();
        Context = CreateContext(Audit, _contextCancellation.Token);
    }

    private SandboxContext Context { get; }
    private InMemoryAuditSink Audit { get; }

    private SandboxContext CreateContext(InMemoryAuditSink audit, CancellationToken token)
        => new(SandboxRunId.New(), _policy, new ResourceMeter(_policy.ResourceLimits),
            new BindingRegistryBuilder().Build(), audit, token);

    private ValueTask<IReadOnlyList<IPAddress>> Resolve(string host, CancellationToken token)
        => _resolution.Resolve(host, token);

    private Task<string> Start(SandboxContext context, CancellationToken token)
        => SafeHttpClient.GetTextAsync(context, new SandboxUri("https://api.example.com/config"),
            _invoker, Resolve, token).AsTask();

    private Task<string> StopWaiting(string mode)
    {
        var request = Start(Context, _callerCancellation.Token);
        if (mode == "caller")
        {
            _callerCancellation.Cancel();
        }
        else if (mode == "context")
        {
            _contextCancellation.Cancel();
        }

        var error = Assert.Throws<SandboxRuntimeException>(() =>
            request.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
        Assert.Equal(mode == "timeout" ? SandboxErrorCode.Timeout : SandboxErrorCode.Cancelled, error.Error.Code);
        Assert.False(_resolution.IsCompleted);
        Assert.True(_resolution.CancellationToken.IsCancellationRequested);
        var audit = Assert.Single(Audit.Events);
        Assert.False(audit.Success);
        Assert.Equal(error.Error.Code, audit.ErrorCode);
        return request;
    }

    private void CompleteAnotherRequest(bool customSource)
    {
        var audit = new InMemoryAuditSink();
        using var context = CreateContext(audit, CancellationToken.None);
        _resolution = new PendingDnsResolution(customSource);
        var request = Start(context, CancellationToken.None);
        _resolution.Succeed();
        Assert.Equal("ok", request.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
        Assert.True(Assert.Single(audit.Events).Success);
        Assert.Equal(1, _resolution.Counter.ResolveCalls);
        Assert.Equal(customSource ? 1 : 0, _resolution.Counter.ResultCalls);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Probe Run(
        string mode, bool customSource, string completion, string marker,
        bool disposeBeforeCompletion = false, bool reuse = false)
    {
        using var fixture = new DnsFailureObservationFixture(mode, customSource);
        var source = fixture._resolution;
        var request = fixture.StopWaiting(mode);
        var events = fixture.Audit.Events.ToArray();
        var read = fixture.Context.Budget.NetworkBytesRead;
        var written = fixture.Context.Budget.NetworkBytesWritten;
        Assert.Equal(0, read);
        Assert.True(written > 0);
        if (disposeBeforeCompletion)
        {
            fixture.Context.Dispose();
        }
        if (reuse)
        {
            fixture.CompleteAnotherRequest(customSource);
            Assert.False(source.IsCompleted);
        }

        object released = source;
        if (completion == "failure")
        {
            var error = new InvalidOperationException(marker);
            source.Fail(error);
            released = error;
        }
        else if (completion == "cancel")
        {
            source.Fail(new OperationCanceledException(marker, new CancellationToken(canceled: true)));
        }
        else
        {
            source.Succeed();
        }

        return new Probe(new WeakReference(released), source.Counter, customSource ? 1 : 0,
            fixture.Audit, fixture.Context.Budget, events, read, written, request);
    }

    public void Dispose()
    {
        Context.Dispose();
        _callerCancellation.Dispose();
        _contextCancellation.Dispose();
        _invoker.Dispose();
    }

    internal sealed record Probe(
        WeakReference Released,
        PendingDnsResolution.ConsumptionCounter Counter,
        int ExpectedConsumptions,
        InMemoryAuditSink Audit,
        ResourceMeter Budget,
        SandboxAuditEvent[] Events,
        long BytesRead,
        long BytesWritten,
        Task<string> Request);
}
