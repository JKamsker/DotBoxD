using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.StraightScalarAssignments;

public sealed class ScalarAssignmentAsyncFallbackTests
{
    [Fact]
    public async Task Pending_binding_assignment_resumes_and_commits_the_raw_target()
    {
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<SandboxValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(PendingI64Binding(invoked, release));
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync(
            StraightScalarAssignmentModules.Assignment(
                "target-dispatch-pending-assignment",
                "I64",
                "I64",
                "value",
                """{ "call": "test.pendingI64", "args": [] }"""));
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(1_000)
            .AllowRuntimeAsync()
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var execution = host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt64(1),
            StraightScalarAssignmentTestSupport.Options()).AsTask();
        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(execution.IsCompleted);
        release.SetResult(SandboxValue.FromInt64(42));
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(42, ((I64Value)result.Value!).Value);
        StraightScalarAssignmentTestSupport.AssertUsage(
            result.ResourceUsage,
            fuel: 6,
            hostCalls: 1);
    }

    private static BindingDescriptor PendingI64Binding(
        TaskCompletionSource invoked,
        TaskCompletionSource<SandboxValue> release)
        => new(
            "test.pendingI64",
            SemVersion.One,
            [],
            SandboxType.I64,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) =>
            {
                invoked.SetResult();
                return new ValueTask<SandboxValue>(release.Task);
            },
            CompiledBinding.RuntimeStub(
                typeof(CompiledRuntime).FullName!,
                nameof(CompiledRuntime.CallBinding)))
        {
            IsAsync = true
        };
}
