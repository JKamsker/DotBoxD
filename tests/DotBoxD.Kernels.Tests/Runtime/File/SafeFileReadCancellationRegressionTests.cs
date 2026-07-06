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
        using var scenario = await CreatePreCanceledReadScenarioAsync();

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await SafeFileSystem.ReadTextAsync(scenario.Context, new SandboxPath("settings.json"), scenario.Token));

        Assert.Equal(SandboxErrorCode.Cancelled, ex.Error.Code);
        AssertNoPreCancellationFileReadSideEffects(scenario.Context, scenario.Audit);
    }

    [Fact]
    public async Task ReadText_descriptor_with_pre_canceled_token_does_not_audit_or_account_file_reads()
    {
        using var scenario = await CreatePreCanceledReadScenarioAsync();

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await SafeFileBindings.ReadText.Invoke(
                scenario.Context,
                [SandboxValue.FromPath("settings.json")],
                scenario.Token));

        Assert.Equal(SandboxErrorCode.Cancelled, ex.Error.Code);
        AssertNoPreCancellationFileReadSideEffects(scenario.Context, scenario.Audit);
    }

    [Fact]
    public async Task ReadTextAsync_with_pre_canceled_operation_token_wraps_and_audits_cancellation()
    {
        using var scenario = await CreateOperationCanceledReadScenarioAsync();

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await SafeFileSystem.ReadTextAsync(
                scenario.Context,
                new SandboxPath("settings.json"),
                scenario.Token));

        Assert.Equal(SandboxErrorCode.Cancelled, ex.Error.Code);
        AssertReadCancellationAudit(scenario.Context, scenario.Audit);
    }

    [Fact]
    public async Task ReadText_descriptor_with_pre_canceled_operation_token_wraps_and_audits_cancellation()
    {
        using var scenario = await CreateOperationCanceledReadScenarioAsync();

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await SafeFileBindings.ReadText.Invoke(
                scenario.Context,
                [SandboxValue.FromPath("settings.json")],
                scenario.Token));

        Assert.Equal(SandboxErrorCode.Cancelled, ex.Error.Code);
        AssertReadCancellationAudit(scenario.Context, scenario.Audit);
    }

    private static Task<ReadCancellationScenario> CreatePreCanceledReadScenarioAsync()
        => CreateReadCancellationScenarioAsync(contextCancellationToken: null);

    private static Task<ReadCancellationScenario> CreateOperationCanceledReadScenarioAsync()
        => CreateReadCancellationScenarioAsync(contextCancellationToken: CancellationToken.None);

    private static async Task<ReadCancellationScenario> CreateReadCancellationScenarioAsync(
        CancellationToken? contextCancellationToken)
    {
        var temp = TempDirectory.Create();
        await System.IO.File.WriteAllTextAsync(Path.Combine(temp.Path, "settings.json"), "tenant-settings");
        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var audit = new InMemoryAuditSink();
        var context = CreateContext(temp.Path, audit, contextCancellationToken ?? cancellation.Token);
        return new ReadCancellationScenario(temp, cancellation, audit, context);
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

    private static void AssertReadCancellationAudit(SandboxContext context, InMemoryAuditSink audit)
    {
        Assert.Equal(0, context.Budget.FileBytesRead);
        var auditEvent = Assert.Single(audit.Events, e => e.BindingId == "file.readText");
        Assert.False(auditEvent.Success);
        Assert.Equal(SandboxErrorCode.Cancelled, auditEvent.ErrorCode);
    }

    private sealed class ReadCancellationScenario(
        TempDirectory temp,
        CancellationTokenSource cancellation,
        InMemoryAuditSink audit,
        SandboxContext context) : IDisposable
    {
        public InMemoryAuditSink Audit => audit;
        public SandboxContext Context => context;
        public CancellationToken Token => cancellation.Token;

        public void Dispose()
        {
            cancellation.Dispose();
            temp.Dispose();
        }
    }
}
