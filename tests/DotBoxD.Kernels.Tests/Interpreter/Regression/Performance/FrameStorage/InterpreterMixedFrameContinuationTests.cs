using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class InterpreterMixedFrameContinuationTests
{
    [Fact]
    public async Task Pending_boxed_assignment_preserves_the_raw_parameter()
    {
        var invoked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<SandboxValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = Host(invoked, release);
        var module = await host.ImportJsonAsync(MixedFrameAssignmentModules.PendingBoxedLocal);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(1_000).AllowRuntimeAsync().Build());

        var execution = host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt64(42),
            Options()).AsTask();
        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(execution.IsCompleted);

        release.SetResult(SandboxValue.FromString("boxed"));
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(42, ((I64Value)result.Value!).Value);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
    }

    private static SandboxHost Host(
        TaskCompletionSource<bool> invoked,
        TaskCompletionSource<SandboxValue> release)
        => SandboxHost.Create(builder =>
        {
            builder.AddBinding(DelayedLabelBinding(invoked, release));
            builder.UseInterpreter();
        });

    private static BindingDescriptor DelayedLabelBinding(
        TaskCompletionSource<bool> invoked,
        TaskCompletionSource<SandboxValue> release)
        => new(
            "test.delayedLabel",
            SemVersion.One,
            [],
            SandboxType.String,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) =>
            {
                invoked.SetResult(true);
                return new ValueTask<SandboxValue>(release.Task);
            },
            CompiledBinding.RuntimeStub(
                typeof(CompiledRuntime).FullName!,
                nameof(CompiledRuntime.CallBinding)))
        {
            IsAsync = true
        };

    private static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };
}
