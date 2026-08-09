using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Runtime.File;

public sealed class SafeFileAuditFailureTests
{
    [Fact]
    public async Task ReadTextAsync_propagates_success_audit_sink_failure_without_retrying_as_file_failure()
    {
        using var temp = TempDirectory.Create();
        await System.IO.File.WriteAllTextAsync(Path.Combine(temp.Path, "settings.txt"), "tenant-settings");
        var audit = new ThrowingAuditSink();
        var context = CreateReadContext(temp.Path, audit);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await SafeFileSystem.ReadTextAsync(
                context,
                new SandboxPath("settings.txt"),
                CancellationToken.None));

        Assert.Same(audit.Sentinel, ex);
        var auditEvent = Assert.Single(audit.Attempts);
        Assert.True(auditEvent.Success);
        Assert.Equal("file.readText", auditEvent.BindingId);
    }

    [Fact]
    public async Task WriteTextAsync_propagates_success_audit_sink_failure_without_retrying_as_file_failure()
    {
        using var temp = TempDirectory.Create();
        var audit = new ThrowingAuditSink();
        var context = CreateWriteContext(temp.Path, audit);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await SafeFileSystem.WriteTextAsync(
                context,
                new SandboxPath("result.txt"),
                "written",
                CancellationToken.None));

        Assert.Same(audit.Sentinel, ex);
        var auditEvent = Assert.Single(audit.Attempts);
        Assert.True(auditEvent.Success);
        Assert.Equal("file.writeText", auditEvent.BindingId);
    }

    private static SandboxContext CreateReadContext(string root, IAuditSink audit)
    {
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileRead(root, maxBytesPerRun: 1024)
            .WithFuel(5_000)
            .Build();

        return CreateContext(policy, audit);
    }

    private static SandboxContext CreateWriteContext(string root, IAuditSink audit)
    {
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileWrite(root, maxBytesPerRun: 1024, allowCreate: true, allowOverwrite: false)
            .WithFuel(5_000)
            .Build();

        return CreateContext(policy, audit);
    }

    private static SandboxContext CreateContext(SandboxPolicy policy, IAuditSink audit)
        => new(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistry([]),
            audit,
            CancellationToken.None);

    private sealed class ThrowingAuditSink : IAuditSink
    {
        private readonly List<SandboxAuditEvent> _attempts = [];

        public InvalidOperationException Sentinel { get; } = new("audit sink failure");

        public IReadOnlyList<SandboxAuditEvent> Attempts => _attempts;

        public long EventsWritten => _attempts.Count;

        public void Write(SandboxAuditEvent auditEvent)
        {
            ArgumentNullException.ThrowIfNull(auditEvent);

            _attempts.Add(auditEvent);
            throw Sentinel;
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
