using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Runtime.File;

public sealed class SafeFileWriteCancellationRegressionTests
{
    [Fact]
    public async Task WriteTextAsync_with_pre_canceled_token_does_not_audit_or_account_file_writes()
    {
        using var temp = TempDirectory.Create();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var audit = new InMemoryAuditSink();
        var context = CreateContext(temp.Path, audit, cancellation.Token);

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await SafeFileSystem.WriteTextAsync(
                context,
                new SandboxPath("result.txt"),
                "hello",
                cancellation.Token));

        Assert.Equal(SandboxErrorCode.Cancelled, ex.Error.Code);
        AssertNoPreCancellationSideEffects(context, audit, temp.Path);
    }

    [Fact]
    public async Task WriteText_descriptor_with_pre_canceled_token_does_not_audit_or_account_file_writes()
    {
        using var temp = TempDirectory.Create();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var audit = new InMemoryAuditSink();
        var context = CreateContext(temp.Path, audit, cancellation.Token);

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await SafeFileBindings.WriteText.Invoke(
                context,
                [new SandboxPathValue(new SandboxPath("result.txt")), SandboxValue.FromString("hello")],
                cancellation.Token));

        Assert.Equal(SandboxErrorCode.Cancelled, ex.Error.Code);
        AssertNoPreCancellationSideEffects(context, audit, temp.Path);
    }

    private static SandboxContext CreateContext(
        string root,
        InMemoryAuditSink audit,
        CancellationToken cancellationToken)
    {
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileWrite(root, maxBytesPerRun: 1024, allowCreate: true, allowOverwrite: false)
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

    private static void AssertNoPreCancellationSideEffects(
        SandboxContext context,
        InMemoryAuditSink audit,
        string root)
    {
        var failures = new List<string>();
        if (context.Budget.FileBytesWritten != 0)
        {
            failures.Add($"expected no file bytes written, observed {context.Budget.FileBytesWritten}");
        }

        var writeAuditCount = audit.Events.Count(e => e.BindingId == "file.writeText");
        if (writeAuditCount != 0)
        {
            failures.Add($"expected no file.writeText audit events, observed {writeAuditCount}");
        }

        if (System.IO.File.Exists(Path.Combine(root, "result.txt")))
        {
            failures.Add("expected no target file to be created");
        }

        Assert.Empty(failures);
    }
}
