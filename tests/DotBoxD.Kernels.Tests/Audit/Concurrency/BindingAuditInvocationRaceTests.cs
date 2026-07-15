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

    public static TheoryData<ExecutionMode> Modes()
        => new()
        {
            ExecutionMode.Interpreted,
            ExecutionMode.Compiled
        };

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Detailed_terminal_audit_wins_before_runtime_failure_fallback(ExecutionMode mode)
    {
        var entered = NewSignal();
        var release = NewSignal();
        var descriptor = Descriptor(async (context, _, _) =>
        {
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            WriteTimeoutAudit(context, "detailed timeout");
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.Timeout,
                "binding timed out"));
        });
        var (host, plan) = await CreateExecutionAsync(descriptor, TimeSpan.FromSeconds(5));

        var execution = ExecuteAsync(host, plan, mode);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        release.TrySetResult();

        var result = await execution.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Timeout, result.Error!.Code);
        var terminal = Assert.Single(result.AuditEvents, IsTimeoutAudit);
        Assert.Equal("detailed timeout", terminal.Message);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Runtime_fallback_wins_and_suppresses_late_terminal_audit(ExecutionMode mode)
    {
        var entered = NewSignal();
        var releaseAudit = NewSignal();
        var auditAttempted = NewSignal();
        IAuditSink? invocationAudit = null;
        var descriptor = Descriptor(async (context, _, cancellationToken) =>
        {
            invocationAudit = context.Audit;
            entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await releaseAudit.Task.ConfigureAwait(false);
                WriteTimeoutAudit(context, "late detailed timeout");
                auditAttempted.TrySetResult();
            }

            return SandboxValue.Unit;
        });
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

    private static async Task<(SandboxHost Host, ExecutionPlan Plan)> CreateExecutionAsync(
        BindingDescriptor descriptor,
        TimeSpan wallTime)
    {
        var limits = new ResourceLimits(
            MaxFuel: 10_000,
            MaxAllocatedBytes: 1_000_000,
            MaxHostCalls: 10,
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

    private static BindingDescriptor Descriptor(BindingInvoker invoke)
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
            IsAsync = true
        };

    private static void WriteTimeoutAudit(SandboxContext context, string message)
    {
        var timestamp = DateTimeOffset.UtcNow;
        context.Audit.Write(new SandboxAuditEvent(
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
