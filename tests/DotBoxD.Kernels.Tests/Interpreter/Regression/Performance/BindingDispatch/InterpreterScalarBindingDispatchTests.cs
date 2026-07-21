using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.BindingDispatch;

public sealed class InterpreterScalarBindingDispatchTests
{
    [Fact]
    public async Task One_argument_binding_uses_scalar_invoker_and_preserves_resource_usage()
    {
        var fastBinding = new FastUnaryBinding();
        var regularBinding = new RegularUnaryBinding();

        var fast = await RunAsync(fastBinding.Descriptor(), InterpreterScalarBindingModules.Unary);
        var regular = await RunAsync(regularBinding.Descriptor(), InterpreterScalarBindingModules.Unary);

        AssertSucceededWithInt32(fast, 41);
        AssertSucceededWithInt32(regular, 41);
        Assert.Equal(regular.ResourceUsage, fast.ResourceUsage);
        Assert.Equal(1, fast.ResourceUsage.HostCalls);
        Assert.Equal(1, fastBinding.FastCalls);
        Assert.Equal(0, fastBinding.ListCalls);
        Assert.Equal(1, regularBinding.Calls);
    }

    [Fact]
    public async Task Two_argument_binding_uses_scalar_invoker_in_argument_order_and_preserves_resource_usage()
    {
        var fastBinding = new FastBinaryBinding();
        var regularBinding = new RegularBinaryBinding();

        var fast = await RunAsync(fastBinding.Descriptor(), InterpreterScalarBindingModules.Binary);
        var regular = await RunAsync(regularBinding.Descriptor(), InterpreterScalarBindingModules.Binary);

        AssertSucceededWithInt32(fast, 42);
        AssertSucceededWithInt32(regular, 42);
        Assert.Equal(regular.ResourceUsage, fast.ResourceUsage);
        Assert.Equal(1, fast.ResourceUsage.HostCalls);
        Assert.Equal(1, fastBinding.FastCalls);
        Assert.Equal(0, fastBinding.ListCalls);
        Assert.Equal(1, regularBinding.Calls);
    }

    [Fact]
    public async Task Regular_binding_fallback_retains_a_distinct_argument_list_per_call()
    {
        var binding = new RetainingBinaryBinding();

        var result = await RunAsync(
            binding.Descriptor(),
            InterpreterScalarBindingModules.RetainedArguments);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Equal(2, result.ResourceUsage.HostCalls);
        Assert.Equal(2, binding.Arguments.Count);
        var first = binding.Arguments[0];
        var second = binding.Arguments[1];
        Assert.NotSame(first, second);
        Assert.Equal([1, 2], Int32Values(first));
        Assert.Equal([3, 4], Int32Values(second));
    }

    [Fact]
    public async Task Local_function_shadows_same_named_fast_binding()
    {
        var binding = new FastUnaryBinding();

        var result = await RunAsync(
            binding.Descriptor("test.shadow"),
            InterpreterScalarBindingModules.LocalFunctionShadowsFastBinding);

        AssertSucceededWithInt32(result, 7);
        Assert.Equal(0, result.ResourceUsage.HostCalls);
        Assert.Equal(0, binding.FastCalls);
        Assert.Equal(0, binding.ListCalls);
    }

    private static async Task<SandboxExecutionResult> RunAsync(
        BindingDescriptor binding,
        string moduleJson)
    {
        using var host = InterpreterScalarBindingModules.CreateHost(binding);
        var plan = await InterpreterScalarBindingModules.PrepareAsync(host, moduleJson);
        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            InterpreterScalarBindingModules.Options());
    }

    private static void AssertSucceededWithInt32(SandboxExecutionResult result, int expected)
    {
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Equal(expected, Assert.IsType<I32Value>(result.Value).Value);
    }

    private static int[] Int32Values(IReadOnlyList<SandboxValue> values)
        => values.Select(value => Assert.IsType<I32Value>(value).Value).ToArray();
}
