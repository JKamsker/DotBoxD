using System.Diagnostics;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Bindings;

public sealed class GenericBindingBoundaryTests
{
    public static TheoryData<ExecutionMode> Modes()
        => new() {
            ExecutionMode.Interpreted,
            ExecutionMode.Compiled
        };

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Generic_binding_timeout_uses_wall_time_budget(ExecutionMode mode)
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(SlowBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(ReturnExpressionModule(
            """{ "call": "test.slow", "args": [] }""",
            "I32"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .AllowRuntimeAsync()
                .WithWallTime(TimeSpan.FromMilliseconds(50))
                .WithFuel(1_000)
                .Build());
        var elapsed = Stopwatch.StartNew();

        var result = await ExecuteAsync(host, plan, mode);

        elapsed.Stop();
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Timeout, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"elapsed {elapsed.Elapsed}");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Binding_operation_canceled_without_host_cancellation_is_sanitized(ExecutionMode mode)
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(SpuriousCanceledBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(ReturnExpressionModule(
            """{ "call": "test.spuriousCancel", "args": [] }""",
            "I32"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await ExecuteAsync(host, plan, mode);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Caller_cancellation_takes_precedence_over_a_binding_fault(ExecutionMode mode)
    {
        using var cancellation = new CancellationTokenSource();
        var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(CancelThenThrowBinding(cancellation));
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(ReturnExpressionModule(
            """{ "call": "test.cancelThenThrow", "args": [] }""",
            "I32"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await ExecuteAsync(host, plan, mode, cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
        Assert.Contains(result.AuditEvents, auditEvent =>
            auditEvent.Kind == "RunSummary" &&
            !auditEvent.Success &&
            auditEvent.ErrorCode == SandboxErrorCode.Cancelled);
        Assert.DoesNotContain(result.AuditEvents, auditEvent =>
            auditEvent.Kind == "RunSummary" &&
            auditEvent.ErrorCode == SandboxErrorCode.BindingFailure);
    }

    private static async Task<SandboxExecutionResult> ExecuteAsync(
        SandboxHost host,
        ExecutionPlan plan,
        ExecutionMode mode,
        CancellationToken cancellationToken = default)
        => await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions
            {
                Mode = mode,
                AllowFallbackToInterpreter = false
            },
            cancellationToken);

    private static BindingDescriptor SlowBinding()
        => new(
            "test.slow",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            async (_, _, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                return SandboxValue.FromInt32(1);
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)))
        { IsAsync = true };

    private static BindingDescriptor SpuriousCanceledBinding()
        => new(
            "test.spuriousCancel",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => throw new OperationCanceledException(),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)));

    private static BindingDescriptor CancelThenThrowBinding(CancellationTokenSource cancellation)
        => new(
            "test.cancelThenThrow",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) =>
            {
                cancellation.Cancel();
                throw new InvalidOperationException();
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)));

    private static string ReturnExpressionModule(string expression, string returnType)
        => $$"""
        {
          "id": "generic-binding-boundary",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "{{returnType}}",
              "body": [{ "op": "return", "value": {{expression}} }]
            }
          ]
        }
        """;
}
