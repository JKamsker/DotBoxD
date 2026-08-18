using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class ResultHookContextFactoryFaultTests
{
    [Hook("test.resultcontextfactory", typeof(TestResult))]
    private sealed record TestEvent;

    private readonly record struct TestResult(bool Success, string? Reason, int Value) : IHookResult;
    private sealed record FaultingContext(HookContext Raw);

    [Fact]
    public async Task FireAsync_reports_context_factory_fault_and_falls_through_to_lower_priority_result()
    {
        var faults = new List<ResultHookFault>();
        using var server = PluginServer.Create(onResultHookFault: faults.Add);
        var adapter = new TestEventAdapter();
        var healthyPipeline = server.Hooks.On<TestEvent>(adapter);
        var factoryFailure = new InvalidOperationException("context factory failure");
        var faultingPipeline = server.Hooks.On<TestEvent, FaultingContext>(adapter, _ => throw factoryFailure);
        var healthyInvocationCount = 0;

        ResultSlot(healthyPipeline).AddDirect(
            priority: 0,
            (_, _, _) =>
            {
                healthyInvocationCount++;
                return ValueTask.FromResult<IHookResult?>(new TestResult(true, null, 42));
            });
        ResultSlot(faultingPipeline).AddDirect(
            priority: 100,
            static (_, _, _) => ValueTask.FromResult<IHookResult?>(null));

        var result = await server.Hooks.FireAsync<TestEvent, TestResult>(new TestEvent());

        Assert.Equal(42, result!.Value.Value);
        Assert.Equal(1, healthyInvocationCount);
        var fault = Assert.Single(faults);
        Assert.Same(factoryFailure, fault.Exception);
        Assert.Equal(typeof(TestEvent), fault.EventType);
    }

    private static ResultHookSlot<TestEvent, TContext> ResultSlot<TContext>(HookPipeline<TestEvent, TContext> pipeline)
        => (ResultHookSlot<TestEvent, TContext>)(typeof(HookPipeline<TestEvent, TContext>)
            .GetField("_resultHooks", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(pipeline) ?? throw new InvalidOperationException("The result-hook slot was not initialized."));

    private sealed class TestEventAdapter : IPluginEventAdapter<TestEvent>
    {
        public string EventName => "test.resultcontextfactory";

        public IReadOnlyList<Parameter> Parameters => [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(TestEvent e) => [];
    }
}
