using System.Collections.Concurrent;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.BindingDispatch;

public sealed class InterpreterScalarBindingContinuationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Pending_operand_is_awaited_before_later_work_and_scalar_binding(int pendingIndex)
    {
        var events = new ConcurrentQueue<string>();
        var first = new OrderedValueBinding(
            "test.first",
            1,
            "first",
            events,
            pending: pendingIndex == 0);
        var second = new OrderedValueBinding(
            "test.second",
            2,
            "second",
            events,
            pending: pendingIndex == 1);
        var ordered = new FastBinaryBinding(() => events.Enqueue("body"));
        using var host = InterpreterScalarBindingModules.CreateHost(
            first.Descriptor(),
            second.Descriptor(),
            ordered.Descriptor("test.ordered"));
        var plan = await InterpreterScalarBindingModules.PrepareAsync(
            host,
            InterpreterScalarBindingModules.OrderedOperands,
            allowAsync: true);

        var execution = host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            InterpreterScalarBindingModules.Options()).AsTask();
        var pending = pendingIndex == 0 ? first : second;
        await pending.Invoked.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(execution.IsCompleted);
        Assert.Equal(
            pendingIndex == 0 ? ["first"] : ["first", "second"],
            events.ToArray());

        pending.Complete();
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(12, Assert.IsType<I32Value>(result.Value).Value);
        Assert.Equal(["first", "second", "body"], events.ToArray());
        Assert.Equal(3, result.ResourceUsage.HostCalls);
        Assert.Equal(1, ordered.FastCalls);
        Assert.Equal(0, ordered.ListCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Pending_ternary_operand_is_awaited_before_later_work_and_scalar_binding(
        int pendingIndex)
    {
        var events = new ConcurrentQueue<string>();
        var first = new OrderedValueBinding(
            "test.first", 1, "first", events, pending: pendingIndex == 0);
        var second = new OrderedValueBinding(
            "test.second", 2, "second", events, pending: pendingIndex == 1);
        var third = new OrderedValueBinding(
            "test.third", 3, "third", events, pending: pendingIndex == 2);
        var ordered = new FastTernaryBinding(() => events.Enqueue("body"));
        using var host = InterpreterScalarBindingModules.CreateHost(
            first.Descriptor(),
            second.Descriptor(),
            third.Descriptor(),
            ordered.Descriptor("test.ordered3"));
        var plan = await InterpreterScalarBindingModules.PrepareAsync(
            host,
            InterpreterScalarBindingModules.OrderedTernaryOperands,
            allowAsync: true);

        var execution = host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            InterpreterScalarBindingModules.Options()).AsTask();
        var pending = pendingIndex switch
        {
            0 => first,
            1 => second,
            _ => third
        };
        await pending.Invoked.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(execution.IsCompleted);
        string[] expectedPrefix = pendingIndex switch
        {
            0 => ["first"],
            1 => ["first", "second"],
            _ => ["first", "second", "third"]
        };
        Assert.Equal(expectedPrefix, events.ToArray());

        pending.Complete();
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(123, Assert.IsType<I32Value>(result.Value).Value);
        Assert.Equal(["first", "second", "third", "body"], events.ToArray());
        Assert.Equal(4, result.ResourceUsage.HostCalls);
        Assert.Equal(1, ordered.FastCalls);
        Assert.Equal(0, ordered.ListCalls);
    }

    [Theory]
    [InlineData("faulted")]
    [InlineData("canceled")]
    public async Task Completed_fast_binding_failure_is_observed_before_async_gate(string completion)
    {
        var binding = new ControlledFastUnaryBinding(isAsync: false);
        binding.Complete(completion);
        using var host = InterpreterScalarBindingModules.CreateHost(binding.Descriptor());
        var plan = await InterpreterScalarBindingModules.PrepareAsync(
            host,
            InterpreterScalarBindingModules.PendingUnary);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            InterpreterScalarBindingModules.Options());

        AssertBindingFailure(result);
        Assert.DoesNotContain("pending result", result.Error!.SafeMessage, StringComparison.Ordinal);
        Assert.Equal(1, binding.FastCalls);
        Assert.Equal(0, binding.ListCalls);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("faulted")]
    [InlineData("canceled")]
    public async Task Pending_fast_binding_completion_preserves_result_or_private_failure(string completion)
    {
        var binding = new ControlledFastUnaryBinding(isAsync: true);
        using var host = InterpreterScalarBindingModules.CreateHost(binding.Descriptor());
        var plan = await InterpreterScalarBindingModules.PrepareAsync(
            host,
            InterpreterScalarBindingModules.PendingUnary,
            allowAsync: true);

        var execution = host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            InterpreterScalarBindingModules.Options()).AsTask();
        await binding.Invoked.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(execution.IsCompleted);

        binding.Complete(completion);
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        if (completion == "success")
        {
            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.Equal(42, Assert.IsType<I32Value>(result.Value).Value);
        }
        else
        {
            AssertBindingFailure(result);
        }

        Assert.Equal(1, binding.FastCalls);
        Assert.Equal(0, binding.ListCalls);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
    }

    [Fact]
    public async Task Pending_fast_binding_observes_caller_cancellation()
    {
        var binding = new ControlledFastUnaryBinding(isAsync: true);
        using var host = InterpreterScalarBindingModules.CreateHost(binding.Descriptor());
        var plan = await InterpreterScalarBindingModules.PrepareAsync(
            host,
            InterpreterScalarBindingModules.PendingUnary,
            allowAsync: true);
        using var cancellation = new CancellationTokenSource();

        var execution = host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            InterpreterScalarBindingModules.Options(),
            cancellation.Token).AsTask();
        await binding.Invoked.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(execution.IsCompleted);

        await cancellation.CancelAsync();
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.Succeeded);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.Equal(1, binding.FastCalls);
        Assert.Equal(0, binding.ListCalls);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
    }

    private static void AssertBindingFailure(SandboxExecutionResult result)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
        Assert.Equal("binding 'test.pendingUnary' failed", result.Error.SafeMessage);
        Assert.DoesNotContain("secret binding failure", result.Error.SafeMessage, StringComparison.Ordinal);
    }
}
