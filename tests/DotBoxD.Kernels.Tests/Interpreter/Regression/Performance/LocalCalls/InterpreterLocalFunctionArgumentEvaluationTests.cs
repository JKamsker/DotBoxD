using System.Collections.Concurrent;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.LocalCalls;

public sealed class InterpreterLocalFunctionArgumentEvaluationTests
{
    [Fact]
    public async Task Synchronous_arguments_are_evaluated_left_to_right_exactly_once()
    {
        var events = new ConcurrentQueue<string>();
        using var host = Host(
            CompletedBinding("test.first", 1, () => events.Enqueue("first")),
            CompletedBinding("test.second", 2, () => events.Enqueue("second")),
            BodyBinding(() => events.Enqueue("body")));
        var plan = await PrepareAsync(host, async: false);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, Options());

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(12, ((I32Value)result.Value!).Value);
        Assert.Equal(["first", "second", "body"], events);
        Assert.Equal(3, result.ResourceUsage.HostCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Pending_argument_awaits_before_later_work_and_then_preserves_order(int pendingIndex)
    {
        var events = new ConcurrentQueue<string>();
        var pendingInvoked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<SandboxValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = pendingIndex == 0
            ? PendingBinding("test.first", "first", events, pendingInvoked, release)
            : CompletedBinding("test.first", 1, () => events.Enqueue("first"));
        var second = pendingIndex == 1
            ? PendingBinding("test.second", "second", events, pendingInvoked, release)
            : CompletedBinding("test.second", 2, () => events.Enqueue("second"));
        using var host = Host(first, second, BodyBinding(() => events.Enqueue("body")));
        var plan = await PrepareAsync(host, async: true);

        var execution = host.ExecuteAsync(plan, "main", SandboxValue.Unit, Options()).AsTask();
        await pendingInvoked.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(execution.IsCompleted);
        Assert.Equal(
            pendingIndex == 0 ? ["first"] : ["first", "second"],
            events);

        release.SetResult(SandboxValue.FromInt32(pendingIndex + 1));
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(12, ((I32Value)result.Value!).Value);
        Assert.Equal(["first", "second", "body"], events);
        Assert.Equal(3, result.ResourceUsage.HostCalls);
    }

    private static SandboxHost Host(
        BindingDescriptor first,
        BindingDescriptor second,
        BindingDescriptor body)
        => SandboxHost.Create(builder =>
        {
            builder.AddBinding(first);
            builder.AddBinding(second);
            builder.AddBinding(body);
            builder.UseInterpreter();
        });

    private static async Task<ExecutionPlan> PrepareAsync(SandboxHost host, bool async)
    {
        var module = await host.ImportJsonAsync(LocalFunctionScalarCallModules.OrderedArguments);
        var policy = SandboxPolicyBuilder.Create().WithFuel(1_000);
        if (async)
        {
            policy.AllowRuntimeAsync();
        }

        return await host.PrepareAsync(module, policy.Build());
    }

    private static BindingDescriptor CompletedBinding(string id, int value, Action invoked)
        => I32Binding(
            id,
            () =>
            {
                invoked();
                return ValueTask.FromResult(SandboxValue.FromInt32(value));
            },
            isAsync: false);

    private static BindingDescriptor PendingBinding(
        string id,
        string eventName,
        ConcurrentQueue<string> events,
        TaskCompletionSource<bool> pendingInvoked,
        TaskCompletionSource<SandboxValue> release)
        => I32Binding(
            id,
            () =>
            {
                events.Enqueue(eventName);
                pendingInvoked.SetResult(true);
                return new ValueTask<SandboxValue>(release.Task);
            },
            isAsync: true);

    private static BindingDescriptor I32Binding(
        string id,
        Func<ValueTask<SandboxValue>> invoke,
        bool isAsync)
        => new(
            id,
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => invoke(),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)))
        {
            IsAsync = isAsync
        };

    private static BindingDescriptor BodyBinding(Action invoked)
        => new(
            "test.observeBody",
            SemVersion.One,
            [SandboxType.I32, SandboxType.I32],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, args, _) =>
            {
                invoked();
                var first = ((I32Value)args[0]).Value;
                var second = ((I32Value)args[1]).Value;
                return ValueTask.FromResult(SandboxValue.FromInt32((first * 10) + second));
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };
}
