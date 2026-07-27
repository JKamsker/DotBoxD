using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.LocalCalls.InlineI32;

public sealed class InlineI32LocalFunctionSemanticsTests
{
    [Theory]
    [InlineData("""{ "op": "add", "left": { "var": "operand" }, "right": { "i32": 1 } }""", 41, 42, 11L)]
    [InlineData("""{ "op": "sub", "left": { "var": "operand" }, "right": { "i32": 8 } }""", 50, 42, 11L)]
    [InlineData("""{ "op": "mul", "left": { "var": "operand" }, "right": { "i32": 2 } }""", 21, 42, 11L)]
    [InlineData("""{ "op": "div", "left": { "var": "operand" }, "right": { "i32": 2 } }""", 84, 42, 11L)]
    [InlineData("""{ "op": "rem", "left": { "var": "operand" }, "right": { "i32": 44 } }""", 86, 42, 11L)]
    [InlineData("""{ "unary": "-", "operand": { "var": "operand" } }""", -42, 42, 10L)]
    [InlineData("""{ "op": "add", "left": { "op": "mul", "left": { "var": "operand" }, "right": { "i32": 2 } }, "right": { "i32": 2 } }""", 20, 42, 13L)]
    public async Task Supported_values_operators_and_metering_match_generic_execution(
        string expression,
        int input,
        int expected,
        long expectedFuel)
    {
        using var host = SandboxTestHost.Create();
        var plan = await InlineI32LocalFunctionTestSupport.PrepareAsync(
            host,
            InlineI32LocalFunctionModules.SingleCall("inline-i32-operator", expression));

        var result = await InlineI32LocalFunctionTestSupport.ExecuteAsync(plan, input);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expected, InlineI32LocalFunctionTestSupport.ReadInt32(result));
        InlineI32LocalFunctionTestSupport.AssertUsage(result.ResourceUsage, expectedFuel);
    }

    [Fact]
    public async Task Literal_argument_is_evaluated_once_without_reading_the_caller_parameter()
    {
        using var host = SandboxTestHost.Create();
        var plan = await InlineI32LocalFunctionTestSupport.PrepareAsync(
            host,
            InlineI32LocalFunctionModules.SingleCall(
                "inline-i32-literal-argument",
                """{ "op": "add", "left": { "var": "operand" }, "right": { "i32": 3 } }""",
                """{ "i32": 7 }"""));

        var result = await InlineI32LocalFunctionTestSupport.ExecuteAsync(plan, input: 100);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(10, InlineI32LocalFunctionTestSupport.ReadInt32(result));
        InlineI32LocalFunctionTestSupport.AssertUsage(result.ResourceUsage, fuel: 11);
    }

    [Fact]
    public async Task Shared_plan_keeps_concurrent_arguments_and_frames_isolated()
    {
        const int executionCount = 32;
        using var host = SandboxTestHost.Create();
        var plan = await InlineI32LocalFunctionTestSupport.PrepareAsync(
            host,
            InlineI32LocalFunctionModules.SingleCall(
                "inline-i32-concurrent",
                """{ "op": "add", "left": { "op": "mul", "left": { "var": "operand" }, "right": { "i32": 3 } }, "right": { "i32": 7 } }"""));
        var interpreter = new SandboxInterpreter();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, executionCount)
            .Select(input => Task.Run(async () =>
            {
                await start.Task;
                return await interpreter.ExecuteAsync(
                    plan,
                    "main",
                    SandboxValue.FromInt32(input),
                    InlineI32LocalFunctionTestSupport.Options(),
                    CancellationToken.None);
            }))
            .ToArray();

        start.SetResult();
        var results = await Task.WhenAll(tasks);

        for (var input = 0; input < results.Length; input++)
        {
            Assert.True(results[input].Succeeded, results[input].Error?.SafeMessage);
            Assert.Equal((input * 3) + 7, InlineI32LocalFunctionTestSupport.ReadInt32(results[input]));
            InlineI32LocalFunctionTestSupport.AssertUsage(results[input].ResourceUsage, fuel: 13);
        }
    }
}
