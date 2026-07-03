using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Runtime.File;

public sealed class SafeFileReadCancellationRegressionTests
{
    [Fact]
    public async Task ReadTextAsync_with_pre_canceled_token_does_not_audit_or_account_file_reads()
    {
        using var temp = TempDirectory.Create();
        await System.IO.File.WriteAllTextAsync(Path.Combine(temp.Path, "settings.json"), "tenant-settings");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var audit = new InMemoryAuditSink();
        var context = CreateContext(temp.Path, audit, cancellation.Token);

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await SafeFileSystem.ReadTextAsync(context, new SandboxPath("settings.json"), cancellation.Token));

        Assert.Equal(SandboxErrorCode.Cancelled, ex.Error.Code);
        AssertNoPreCancellationFileReadSideEffects(context, audit);
    }

    [Fact]
    public async Task ReadText_descriptor_with_pre_canceled_token_does_not_audit_or_account_file_reads()
    {
        using var temp = TempDirectory.Create();
        await System.IO.File.WriteAllTextAsync(Path.Combine(temp.Path, "settings.json"), "tenant-settings");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var audit = new InMemoryAuditSink();
        var context = CreateContext(temp.Path, audit, cancellation.Token);

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await SafeFileBindings.ReadText.Invoke(
                context,
                [SandboxValue.FromPath("settings.json")],
                cancellation.Token));

        Assert.Equal(SandboxErrorCode.Cancelled, ex.Error.Code);
        AssertNoPreCancellationFileReadSideEffects(context, audit);
    }

    private static SandboxContext CreateContext(
        string root,
        InMemoryAuditSink audit,
        CancellationToken cancellationToken)
    {
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileRead(root, maxBytesPerRun: 1024)
            .WithFuel(5_000)
            .Build();
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistry([]),
            audit,
            cancellationToken);
    }

    private static void AssertNoPreCancellationFileReadSideEffects(
        SandboxContext context,
        InMemoryAuditSink audit)
    {
        var failures = new List<string>();
        if (context.Budget.FileBytesRead != 0)
        {
            failures.Add($"expected no file bytes read, observed {context.Budget.FileBytesRead}");
        }

        var auditCount = audit.Events.Count(e => e.Kind == "BindingCall" && e.BindingId == "file.readText");
        if (auditCount != 0)
        {
            failures.Add($"expected no file.readText audit events, observed {auditCount}");
        }

        Assert.Empty(failures);
    }
}
