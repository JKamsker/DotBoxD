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
            CompletedBinding("test.third", 3, () => events.Enqueue("third")),
            BodyBinding(() => events.Enqueue("body")));
        var plan = await PrepareAsync(host, async: false);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, Options());

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(123, ((I32Value)result.Value!).Value);
        Assert.Equal(["first", "second", "third", "body"], events);
        Assert.Equal(4, result.ResourceUsage.HostCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
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
        var third = pendingIndex == 2
            ? PendingBinding("test.third", "third", events, pendingInvoked, release)
            : CompletedBinding("test.third", 3, () => events.Enqueue("third"));
        using var host = Host(first, second, third, BodyBinding(() => events.Enqueue("body")));
        var plan = await PrepareAsync(host, async: true);

        var execution = host.ExecuteAsync(plan, "main", SandboxValue.Unit, Options()).AsTask();
        await pendingInvoked.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(execution.IsCompleted);
        Assert.Equal(
            pendingIndex switch
            {
                0 => ["first"],
                1 => ["first", "second"],
                _ => ["first", "second", "third"]
            },
            events);

        release.SetResult(SandboxValue.FromInt32(pendingIndex + 1));
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(123, ((I32Value)result.Value!).Value);
        Assert.Equal(["first", "second", "third", "body"], events);
        Assert.Equal(4, result.ResourceUsage.HostCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Failed_argument_stops_later_arguments_and_the_function_body(int failedIndex)
    {
        var events = new ConcurrentQueue<string>();
        var first = failedIndex == 0
            ? FailedBinding("test.first", "first", events)
            : CompletedBinding("test.first", 1, () => events.Enqueue("first"));
        var second = failedIndex == 1
            ? FailedBinding("test.second", "second", events)
            : CompletedBinding("test.second", 2, () => events.Enqueue("second"));
        var third = failedIndex == 2
            ? FailedBinding("test.third", "third", events)
            : CompletedBinding("test.third", 3, () => events.Enqueue("third"));
        using var host = Host(first, second, third, BodyBinding(() => events.Enqueue("body")));
        var plan = await PrepareAsync(host, async: false);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, Options());

        Assert.False(result.Succeeded);
        Assert.Equal(
            failedIndex switch
            {
                0 => ["first"],
                1 => ["first", "second"],
                _ => ["first", "second", "third"]
            },
            events);
        Assert.Equal(failedIndex + 1, result.ResourceUsage.HostCalls);
    }

    [Fact]
    public async Task Pending_function_body_retains_all_three_arguments_for_later_statements()
    {
        var bodyInvoked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<SandboxValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(PendingUnitBinding(bodyInvoked, release));
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync(LocalFunctionTripleCallModules.PendingBody);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(1_000).AllowRuntimeAsync().Build());

        var execution = host.ExecuteAsync(plan, "main", SandboxValue.Unit, Options()).AsTask();
        await bodyInvoked.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(execution.IsCompleted);
        release.SetResult(SandboxValue.Unit);
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(123, ((I32Value)result.Value!).Value);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
    }

    private static SandboxHost Host(
        BindingDescriptor first,
        BindingDescriptor second,
        BindingDescriptor third,
        BindingDescriptor body)
        => SandboxHost.Create(builder =>
        {
            builder.AddBinding(first);
            builder.AddBinding(second);
            builder.AddBinding(third);
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

    private static BindingDescriptor FailedBinding(
        string id,
        string eventName,
        ConcurrentQueue<string> events)
        => I32Binding(
            id,
            () =>
            {
                events.Enqueue(eventName);
                return ValueTask.FromException<SandboxValue>(
                    new InvalidOperationException($"{eventName} failed"));
            },
            isAsync: false);

    private static BindingDescriptor PendingUnitBinding(
        TaskCompletionSource<bool> bodyInvoked,
        TaskCompletionSource<SandboxValue> release)
        => new(
            "test.pause",
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) =>
            {
                bodyInvoked.SetResult(true);
                return new ValueTask<SandboxValue>(release.Task);
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)))
        {
            IsAsync = true
        };

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
            [SandboxType.I32, SandboxType.I32, SandboxType.I32],
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
                var third = ((I32Value)args[2]).Value;
                return ValueTask.FromResult(SandboxValue.FromInt32((first * 100) + (second * 10) + third));
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
