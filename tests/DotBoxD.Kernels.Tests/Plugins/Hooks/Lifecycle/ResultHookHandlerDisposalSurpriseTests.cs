using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class ResultHookHandlerDisposalSurpriseTests
{
    [Hook("test.resulthandlerdisposal", typeof(TestResult))]
    private sealed record TestEvent;

    private readonly record struct TestResult(bool Success, string? Reason, int Value) : IHookResult;

    [Fact]
    public async Task FireAsync_stops_before_fallback_when_abstaining_handler_disposes_server()
    {
        using var server = PluginServer.Create();
        var fallbackInvoked = false;
        var pipeline = server.Hooks.On<TestEvent>(new TestEventAdapter());

        ResultSlot(pipeline).AddDirect(
            priority: 100,
            (_, _, _) =>
            {
                server.Dispose();
                return ValueTask.FromResult<IHookResult?>(new TestResult(false, "abstained", 0));
            });
        ResultSlot(pipeline).AddDirect(
            priority: 0,
            (_, _, _) =>
            {
                fallbackInvoked = true;
                return ValueTask.FromResult<IHookResult?>(new TestResult(true, null, 42));
            });

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await server.Hooks.FireAsync<TestEvent, TestResult>(new TestEvent()));

        Assert.False(fallbackInvoked);
    }

    private static ResultHookSlot<TestEvent, HookContext> ResultSlot(HookPipeline<TestEvent, HookContext> pipeline)
        => (ResultHookSlot<TestEvent, HookContext>)(typeof(HookPipeline<TestEvent, HookContext>)
            .GetField("_resultHooks", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(pipeline) ?? throw new InvalidOperationException("The result-hook slot was not initialized."));

    private sealed class TestEventAdapter : IPluginEventAdapter<TestEvent>
    {
        public string EventName => "test.resulthandlerdisposal";

        public IReadOnlyList<Parameter> Parameters => [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(TestEvent e) => [];
    }
}
