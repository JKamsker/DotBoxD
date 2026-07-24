using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks.Performance;

internal static class ResultHookFanoutTestSupport
{
    public static readonly ProbeEventAdapter Adapter = new();
    public static readonly ProbeEvent Event = new(1);

    public static void AddPipelines(HookRegistry hooks, int count, bool includeHandlers)
    {
        AddPipeline(hooks.On<ProbeEvent>(Adapter), includeHandlers);
        if (count == 1)
        {
            return;
        }

        AddPipeline(hooks.On<ProbeEvent, Context1>(Adapter, static raw => new Context1(raw)), includeHandlers);
        if (count == 2)
        {
            return;
        }

        if (count != 8)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Expected one, two, or eight pipelines.");
        }

        AddPipeline(hooks.On<ProbeEvent, Context2>(Adapter, static raw => new Context2(raw)), includeHandlers);
        AddPipeline(hooks.On<ProbeEvent, Context3>(Adapter, static raw => new Context3(raw)), includeHandlers);
        AddPipeline(hooks.On<ProbeEvent, Context4>(Adapter, static raw => new Context4(raw)), includeHandlers);
        AddPipeline(hooks.On<ProbeEvent, Context5>(Adapter, static raw => new Context5(raw)), includeHandlers);
        AddPipeline(hooks.On<ProbeEvent, Context6>(Adapter, static raw => new Context6(raw)), includeHandlers);
        AddPipeline(hooks.On<ProbeEvent, Context7>(Adapter, static raw => new Context7(raw)), includeHandlers);
    }

    public static void AddPipeline<TContext>(
        HookPipeline<ProbeEvent, TContext> pipeline,
        bool includeHandler)
    {
        if (includeHandler)
        {
            AddHandler(
                pipeline,
                priority: 0,
                static (_, _, _) => ValueTask.FromResult<IHookResult?>(null));
        }
    }

    public static void AddResult<TContext>(
        HookPipeline<ProbeEvent, TContext> pipeline,
        int priority,
        int value)
    {
        var result = new ProbeResult(Success: true, Value: value);
        AddHandler(
            pipeline,
            priority,
            (_, _, _) => ValueTask.FromResult<IHookResult?>(result));
    }

    public static void AddHandler<TContext>(
        HookPipeline<ProbeEvent, TContext> pipeline,
        int priority,
        Func<ProbeEvent, TContext, CancellationToken, ValueTask<IHookResult?>> handler)
        => ResultSlot(pipeline).AddDirect(priority, handler);

    public static void AddSandboxHandler<TContext>(
        HookPipeline<ProbeEvent, TContext> pipeline,
        InstalledKernel kernel,
        int priority)
        => ResultSlot(pipeline).AddSandbox(
            kernel,
            priority,
            static _ => new ProbeResult(Success: true, Value: 0));

    public static object PipelineGate<TContext>(HookPipeline<ProbeEvent, TContext> pipeline)
        => PipelineFields<TContext>.Gate.GetValue(pipeline) ??
            throw new InvalidOperationException("The hook-pipeline gate was not initialized.");

    public static void ClearResultRegistrationSnapshot<TContext>(HookPipeline<ProbeEvent, TContext> pipeline)
        => PipelineFields<TContext>.ResultRegistrationSnapshot.SetValue(pipeline, null);

    private static ResultHookSlot<ProbeEvent, TContext> ResultSlot<TContext>(
        HookPipeline<ProbeEvent, TContext> pipeline) =>
        // Reflection is setup-only and keeps tests on the real registry path without adding a production test seam.
        (ResultHookSlot<ProbeEvent, TContext>)(ResultSlotField<TContext>.Value.GetValue(pipeline) ??
            throw new InvalidOperationException("The result-hook slot was not initialized."));

    private static class ResultSlotField<TContext>
    {
        internal static readonly FieldInfo Value =
            typeof(HookPipeline<ProbeEvent, TContext>).GetField(
                "_resultHooks",
                BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException("The result-hook slot field could not be found.");
    }

    private static class PipelineFields<TContext>
    {
        internal static readonly FieldInfo Gate =
            typeof(HookPipeline<ProbeEvent, TContext>).GetField(
                "_gate",
                BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException("The hook-pipeline gate field could not be found.");

        internal static readonly FieldInfo ResultRegistrationSnapshot =
            typeof(HookPipeline<ProbeEvent, TContext>).GetField(
                "_resultRegistrationSnapshot",
                BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException("The result-registration snapshot field could not be found.");
    }

    internal sealed class ProbeEventAdapter : IPluginEventAdapter<ProbeEvent>
    {
        public string EventName => "test.result-hook-fanout";

        public IReadOnlyList<Parameter> Parameters => [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(ProbeEvent e) => [];
    }

    internal sealed class InvocationCounter
    {
        public int Value;
    }

    internal readonly record struct ProbeEvent(int Value);
    internal readonly record struct ProbeResult(bool Success, int Value) : IHookResult;
    internal readonly record struct Context1(HookContext Raw);
    internal readonly record struct Context2(HookContext Raw);
    internal readonly record struct Context3(HookContext Raw);
    internal readonly record struct Context4(HookContext Raw);
    internal readonly record struct Context5(HookContext Raw);
    internal readonly record struct Context6(HookContext Raw);
    internal readonly record struct Context7(HookContext Raw);
}

internal sealed class ResultHookFanoutScenario : IDisposable
{
    private readonly PluginServer _server;

    private ResultHookFanoutScenario(PluginServer server)
        => _server = server;

    public int DispatchCount { get; private set; }

    public static ResultHookFanoutScenario Create(int pipelineCount, bool includeHandlers)
    {
        var server = PluginServer.Create();
        ResultHookFanoutTestSupport.AddPipelines(server.Hooks, pipelineCount, includeHandlers);
        return new ResultHookFanoutScenario(server);
    }

    public void Dispatch()
    {
        var result = _server.Hooks
            .FireAsync<ResultHookFanoutTestSupport.ProbeEvent, ResultHookFanoutTestSupport.ProbeResult>(
                ResultHookFanoutTestSupport.Event)
            .GetAwaiter()
            .GetResult();
        if (result is not null)
        {
            throw new InvalidOperationException("The allocation-test handler unexpectedly produced a result.");
        }

        DispatchCount++;
    }

    public void Dispose() => _server.Dispose();
}
