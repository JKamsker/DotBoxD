using System.Net;
using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using static DotBoxD.Kernels.Tests._TestSupport.NetworkTestFixtures;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class SafeHttpAuditFailureTests
{
    [Fact]
    public async Task GetTextAsync_does_not_retry_audit_sink_failures_as_http_failures()
    {
        var sentinel = new InvalidOperationException("audit sink failed");
        var audit = new ThrowingAuditSink(sentinel);
        using var context = CreateContext(audit);
        using var invoker = FakeInvoker("ok");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await SafeHttpClient.GetTextAsync(
                context,
                new SandboxUri("https://api.example.com/config"),
                invoker,
                StaticDns(IPAddress.Parse("93.184.216.34")),
                CancellationToken.None));

        Assert.Same(sentinel, ex);
        Assert.Equal(1, audit.WriteCalls);
        var auditEvent = Assert.Single(audit.Attempts);
        Assert.True(auditEvent.Success);
        Assert.Null(auditEvent.ErrorCode);
    }

    private static SandboxContext CreateContext(IAuditSink audit)
    {
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();

        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistryBuilder().Build(),
            audit,
            CancellationToken.None);
    }

    private sealed class ThrowingAuditSink(Exception exception) : IAuditSink
    {
        private readonly List<SandboxAuditEvent> _attempts = [];

        public IReadOnlyList<SandboxAuditEvent> Attempts => _attempts;

        public int WriteCalls { get; private set; }

        public long EventsWritten => WriteCalls;

        public void Write(SandboxAuditEvent auditEvent)
        {
            WriteCalls++;
            _attempts.Add(auditEvent);
            throw exception;
        }

        public bool HasBindingAuditSince(
            BindingDescriptor descriptor,
            long checkpoint,
            bool success,
            SandboxErrorCode? expectedErrorCode,
            SandboxRunId runId,
            string moduleHash,
            string policyHash)
            => false;
    }
}
