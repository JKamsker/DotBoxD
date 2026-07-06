using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Runtime;

public sealed class PluginEventValueWriterCallbackValidationTests
{
    [Theory]
    [InlineData(KernelOperation.Handle)]
    [InlineData(KernelOperation.ShouldHandle)]
    public async Task Writer_ToSandboxValue_failures_are_reported_as_adapter_validation(KernelOperation operation)
    {
        var messages = new InMemoryPluginMessageSink();
        var kernel = await InstallAsync(messages, SingleValuePackage());
        var adapter = new ThrowingWriterAdapter(ThrowingWriterCallback.ToSandboxValue, SingleValueParameters);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await InvokeAsync(kernel, adapter, operation));

        Assert.Contains(ex.Diagnostics, diagnostic => diagnostic.Code == "DBXK036");
        Assert.Empty(messages.Messages);
        Assert.Empty(kernel.ExecutionObservations);
    }

    [Theory]
    [InlineData(KernelOperation.Handle)]
    [InlineData(KernelOperation.ShouldHandle)]
    public async Task Writer_CopySandboxValues_failures_are_reported_as_adapter_validation(KernelOperation operation)
    {
        var messages = new InMemoryPluginMessageSink();
        var kernel = await InstallAsync(messages, MultiValuePackage());
        var adapter = new ThrowingWriterAdapter(ThrowingWriterCallback.CopySandboxValues, MultiValueParameters);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await InvokeAsync(kernel, adapter, operation));

        Assert.Contains(ex.Diagnostics, diagnostic => diagnostic.Code == "DBXK036");
        Assert.Empty(messages.Messages);
        Assert.Empty(kernel.ExecutionObservations);
    }

    [Theory]
    [InlineData(KernelOperation.Handle)]
    [InlineData(KernelOperation.ShouldHandle)]
    public async Task Writer_EventValueCount_failures_are_reported_as_adapter_validation(KernelOperation operation)
    {
        var messages = new InMemoryPluginMessageSink();
        var kernel = await InstallAsync(messages, SingleValuePackage());
        var adapter = new ThrowingWriterAdapter(ThrowingWriterCallback.EventValueCount, SingleValueParameters);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await InvokeAsync(kernel, adapter, operation));

        Assert.Contains(ex.Diagnostics, diagnostic => diagnostic.Code == "DBXK036");
        Assert.Empty(messages.Messages);
        Assert.Empty(kernel.ExecutionObservations);
    }

    private static Parameter[] SingleValueParameters { get; } = [new("e_TargetId", SandboxType.String)];

    private static Parameter[] MultiValueParameters { get; } = [
        new("e_TargetId", SandboxType.String),
        new("e_Count", SandboxType.I32)
    ];

    private static async Task<InstalledKernel> InstallAsync(InMemoryPluginMessageSink messages, PluginPackage package)
    {
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        return await server.InstallAsync(package);
    }

    private static async Task InvokeAsync(
        InstalledKernel kernel,
        IPluginEventAdapter<WriterCallbackEvent> adapter,
        KernelOperation operation)
    {
        var e = new WriterCallbackEvent("player-1", 7);
        if (operation == KernelOperation.Handle)
        {
            await kernel.HandleAsync(adapter, e);
            return;
        }

        _ = await kernel.ShouldHandleAsync(adapter, e);
    }

    private static PluginPackage SingleValuePackage()
        => Package("writer-callback-single", SingleValueParameters);

    private static PluginPackage MultiValuePackage()
        => Package("writer-callback-multi", MultiValueParameters);

    private static PluginPackage Package(string pluginId, Parameter[] parameters)
    {
        var span = new SourceSpan(1, 1);
        return PluginPackage.Create(
            new PluginManifest(
                pluginId,
                "IEventKernel<WriterCallbackEvent>",
                ExecutionMode.Interpreted,
                ["Cpu", "Alloc", "HostStateWrite", "Concurrency", "Audit"],
                [],
                [new HookSubscriptionManifest(nameof(WriterCallbackEvent), "WriterCallbackKernel")])
            {
                RequiredCapabilities = [RuntimeCapabilityIds.Async, PluginMessageBindings.CapabilityId]
            },
            new SandboxModule(
                pluginId,
                SemVersion.One,
                SemVersion.One,
                [new CapabilityRequest(PluginMessageBindings.CapabilityId, "test notification")],
                [
                    new SandboxFunction(
                        "ShouldHandle",
                        true,
                        parameters,
                        SandboxType.Bool,
                        [new ReturnStatement(new LiteralExpression(SandboxValue.FromBool(true), span), span)]),
                    new SandboxFunction(
                        "Handle",
                        true,
                        parameters,
                        SandboxType.Unit,
                        [
                            new ReturnStatement(
                                new CallExpression(
                                    PluginMessageBindings.SendBindingId,
                                    [
                                        new VariableExpression("e_TargetId", span),
                                        new LiteralExpression(SandboxValue.FromString("matched"), span)
                                    ],
                                    null,
                                    span),
                                span)
                        ])
                ],
                new Dictionary<string, string>
                {
                    ["pluginId"] = pluginId,
                    ["kernel"] = "WriterCallbackKernel"
                }));
    }

    private sealed record WriterCallbackEvent(string TargetId, int Count);

    private sealed class ThrowingWriterAdapter(
        ThrowingWriterCallback callback,
        IReadOnlyList<Parameter> parameters) : IPluginEventValueWriter<WriterCallbackEvent>
    {
        public string EventName => nameof(WriterCallbackEvent);

        public IReadOnlyList<Parameter> Parameters { get; } = parameters;

        public int EventValueCount
            => callback == ThrowingWriterCallback.EventValueCount
                ? throw new InvalidOperationException("EventValueCount failed.")
                : Parameters.Count;

        public IReadOnlyList<SandboxValue> ToSandboxValues(WriterCallbackEvent e)
            => throw new InvalidOperationException("Writer adapters should not allocate event value lists.");

        public SandboxValue ToSandboxValue(WriterCallbackEvent e, int index)
        {
            if (callback == ThrowingWriterCallback.ToSandboxValue)
            {
                throw new InvalidOperationException("ToSandboxValue failed.");
            }

            return ValueAt(e, index);
        }

        public void CopySandboxValues(WriterCallbackEvent e, SandboxValue[] destination, int destinationIndex)
        {
            if (callback == ThrowingWriterCallback.CopySandboxValues)
            {
                throw new InvalidOperationException("CopySandboxValues failed.");
            }

            for (var i = 0; i < Parameters.Count; i++)
            {
                destination[destinationIndex + i] = ValueAt(e, i);
            }
        }

        private static SandboxValue ValueAt(WriterCallbackEvent e, int index)
            => index switch
            {
                0 => SandboxValue.FromString(e.TargetId),
                1 => SandboxValue.FromInt32(e.Count),
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
    }

    public enum KernelOperation
    {
        Handle,
        ShouldHandle
    }

    private enum ThrowingWriterCallback
    {
        EventValueCount,
        ToSandboxValue,
        CopySandboxValues
    }
}
