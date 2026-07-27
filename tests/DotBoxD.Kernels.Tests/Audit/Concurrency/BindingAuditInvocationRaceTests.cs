using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Audit;

public sealed class BindingAuditInvocationRaceTests
{
    private const string BindingId = "test.async.audit";

    public static TheoryData<ExecutionMode, bool> Modes()
        => new()
        {
            { ExecutionMode.Interpreted, true },
            { ExecutionMode.Compiled, true },
            { ExecutionMode.Interpreted, false },
            { ExecutionMode.Compiled, false }
        };

    public static TheoryData<ExecutionMode> ExecutionModes()
        => new()
        {
            ExecutionMode.Interpreted,
            ExecutionMode.Compiled
        };

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Detailed_terminal_audit_wins_before_runtime_failure_fallback(
        ExecutionMode mode,
        bool declaresAsync)
    {
        var entered = NewSignal();
        var release = NewSignal();
        var descriptor = Descriptor(async (context, _, _) =>
        {
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            WriteTimeoutAudit(context, context.Audit, "detailed timeout");
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.Timeout,
                "binding timed out"));
        }, declaresAsync);
        var (host, plan) = await CreateExecutionAsync(descriptor, TimeSpan.FromSeconds(5));

        var releaseAfterEntry = ReleaseFromThreadPoolAfterEntryAsync(entered.Task, release);
        var execution = ExecuteAsync(host, plan, mode);
        await releaseAfterEntry.WaitAsync(TimeSpan.FromSeconds(2));
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Timeout, result.Error!.Code);
        var terminal = Assert.Single(result.AuditEvents, IsTimeoutAudit);
        Assert.Equal("detailed timeout", terminal.Message);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Runtime_fallback_wins_and_suppresses_late_terminal_audit(
        ExecutionMode mode,
        bool declaresAsync)
    {
        var entered = NewSignal();
        var releaseAudit = NewSignal();
        var auditAttempted = NewSignal();
        IAuditSink? invocationAudit = null;
        var descriptor = Descriptor(async (context, _, cancellationToken) =>
        {
            var cachedAudit = context.Audit;
            invocationAudit = cachedAudit;
            entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await releaseAudit.Task.ConfigureAwait(false);
                WriteTimeoutAudit(context, context.Audit, "late dynamic timeout");
                WriteTimeoutAudit(context, cachedAudit, "late detailed timeout");
                auditAttempted.TrySetResult();
            }

            return SandboxValue.Unit;
        }, declaresAsync);
        var (host, plan) = await CreateExecutionAsync(descriptor, TimeSpan.FromMilliseconds(100));

        var execution = ExecuteAsync(host, plan, mode);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Timeout, result.Error!.Code);
        var fallback = Assert.Single(result.AuditEvents, IsTimeoutAudit);
        Assert.Equal("binding failed before emitting audit", fallback.Message);
        var eventsBeforeLateWrite = invocationAudit!.EventsWritten;

        releaseAudit.TrySetResult();
        await auditAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(eventsBeforeLateWrite, invocationAudit.EventsWritten);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes))]
    public async Task Reentrant_same_descriptor_preflight_failure_owns_terminal_audit(
        ExecutionMode mode)
    {
        var descriptor = Descriptor(
            static (context, _, _) =>
            {
                _ = CompiledRuntime.CallBinding(context, BindingId, []);
                return ValueTask.FromResult(SandboxValue.Unit);
            },
            isAsync: false);
        var (host, plan) = await CreateExecutionAsync(
            descriptor,
            TimeSpan.FromSeconds(5),
            maxHostCalls: 1);

        var result = await ExecuteAsync(host, plan, mode).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        var terminal = result.AuditEvents.Where(auditEvent =>
            auditEvent.BindingId == BindingId &&
            !auditEvent.Success &&
            auditEvent.ErrorCode == SandboxErrorCode.QuotaExceeded).ToArray();
        Assert.Equal(2, terminal.Length);
        Assert.All(terminal, auditEvent =>
            Assert.Equal("binding failed before emitting audit", auditEvent.Message));
        Assert.Equal(2, terminal.Select(auditEvent => auditEvent.SequenceNumber).Distinct().Count());
    }

    private static async Task<(SandboxHost Host, ExecutionPlan Plan)> CreateExecutionAsync(
        BindingDescriptor descriptor,
        TimeSpan wallTime,
        int maxHostCalls = 10)
    {
        var limits = new ResourceLimits(
            MaxFuel: 10_000,
            MaxAllocatedBytes: 1_000_000,
            MaxHostCalls: maxHostCalls,
            MaxWallTime: wallTime);
        var policy = SandboxPolicyBuilder.Create().AllowRuntimeAsync().Build() with
        {
            ResourceLimits = limits
        };
        var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(descriptor);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(ModuleJson());
        var plan = await host.PrepareAsync(module, policy);
        return (host, plan);
    }

    private static Task<SandboxExecutionResult> ExecuteAsync(
        SandboxHost host,
        ExecutionPlan plan,
        ExecutionMode mode)
        => host.ExecuteAsync(
                plan,
                "main",
                SandboxValue.Unit,
                new SandboxExecutionOptions
                {
                    Mode = mode,
                    AllowFallbackToInterpreter = false
                })
            .AsTask();

    private static BindingDescriptor Descriptor(BindingInvoker invoke, bool isAsync)
        => new(
            BindingId,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.PureHostFacade,
            invoke,
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)))
        {
            IsAsync = isAsync
        };

    private static void WriteTimeoutAudit(
        SandboxContext context,
        IAuditSink audit,
        string message)
    {
        var timestamp = DateTimeOffset.UtcNow;
        audit.Write(new SandboxAuditEvent(
            context.RunId,
            BindingAuditKinds.BindingCall,
            timestamp,
            Success: false,
            BindingId: BindingId,
            Effect: SandboxEffect.Cpu,
            ResourceId: "test:async-audit",
            ErrorCode: SandboxErrorCode.Timeout,
            Message: message,
            Fields: context.BindingAuditFields("test", timestamp)));
    }

    private static bool IsTimeoutAudit(SandboxAuditEvent auditEvent)
        => auditEvent is
        {
            BindingId: BindingId,
            Success: false,
            ErrorCode: SandboxErrorCode.Timeout
        };

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Task ReleaseFromThreadPoolAfterEntryAsync(
        Task entered,
        TaskCompletionSource release)
        // Compiled under-declared async calls synchronously enter their inline pump,
        // so the releaser must not depend on the blocked xUnit synchronization context.
        => Task.Run(async () =>
        {
            await entered.ConfigureAwait(false);
            release.TrySetResult();
        });

    private static string ModuleJson()
        => """
        {
          "id": "binding-audit-invocation-race",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [{ "op": "return", "value": { "call": "test.async.audit", "args": [] } }]
            }
          ]
        }
        """;
}
