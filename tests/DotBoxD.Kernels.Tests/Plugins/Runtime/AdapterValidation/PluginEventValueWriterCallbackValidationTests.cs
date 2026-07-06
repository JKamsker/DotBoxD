using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Input;

namespace DotBoxD.Kernels.Tests.Plugins.Runtime;

public sealed class PluginEventValueWriterCallbackValidationTests
{
    [Theory]
    [MemberData(nameof(WriterCallbackCases))]
    public async Task Writer_callback_failures_are_reported_as_adapter_validation(
        KernelOperation operation,
        ThrowingWriterCallback callback,
        Parameter[] parameters,
        Func<PluginPackage> packageFactory)
    {
        var messages = new InMemoryPluginMessageSink();
        var kernel = await InstallAsync(messages, packageFactory());
        var adapter = new ThrowingWriterAdapter(callback, parameters);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await InvokeAsync(kernel, adapter, operation));

        Assert.Contains(ex.Diagnostics, diagnostic => diagnostic.Code == "DBXK036");
        Assert.Empty(messages.Messages);
        Assert.Empty(kernel.ExecutionObservations);
    }

    [Fact]
    public void Writer_value_count_is_validated_before_zero_value_fast_path()
    {
        var adapter = new ZeroCountWriterAdapter();

        var ex = Assert.Throws<SandboxValidationException>(
            () => PluginKernelInputBuilder.Build(
                adapter,
                new WriterCallbackEvent("player-1", 7),
                adapter.Parameters,
                [],
                [],
                LiveSettingStore.FromDefinitions([]),
                _ => throw new InvalidOperationException("No deferred updates are expected.")));

        Assert.Contains(ex.Diagnostics, diagnostic => diagnostic.Code == "DBXK036");
    }

    public static IEnumerable<object[]> WriterCallbackCases()
    {
        yield return [
            KernelOperation.Handle,
            ThrowingWriterCallback.ToSandboxValue,
            SingleValueParameters,
            (Func<PluginPackage>)SingleValuePackage
        ];
        yield return [
            KernelOperation.ShouldHandle,
            ThrowingWriterCallback.ToSandboxValue,
            SingleValueParameters,
            (Func<PluginPackage>)SingleValuePackage
        ];
        yield return [
            KernelOperation.Handle,
            ThrowingWriterCallback.CopySandboxValues,
            MultiValueParameters,
            (Func<PluginPackage>)MultiValuePackage
        ];
        yield return [
            KernelOperation.ShouldHandle,
            ThrowingWriterCallback.CopySandboxValues,
            MultiValueParameters,
            (Func<PluginPackage>)MultiValuePackage
        ];
        yield return [
            KernelOperation.Handle,
            ThrowingWriterCallback.EventValueCount,
            SingleValueParameters,
            (Func<PluginPackage>)SingleValuePackage
        ];
        yield return [
            KernelOperation.ShouldHandle,
            ThrowingWriterCallback.EventValueCount,
            SingleValueParameters,
            (Func<PluginPackage>)SingleValuePackage
        ];
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

    private sealed class ZeroCountWriterAdapter : IPluginEventValueWriter<WriterCallbackEvent>
    {
        public string EventName => nameof(WriterCallbackEvent);

        public IReadOnlyList<Parameter> Parameters { get; } = SingleValueParameters;

        public int EventValueCount => 0;

        public IReadOnlyList<SandboxValue> ToSandboxValues(WriterCallbackEvent e)
            => throw new InvalidOperationException("Writer adapters should not allocate event value lists.");

        public SandboxValue ToSandboxValue(WriterCallbackEvent e, int index)
            => throw new InvalidOperationException("The zero-value fast path must reject before reading values.");

        public void CopySandboxValues(WriterCallbackEvent e, SandboxValue[] destination, int destinationIndex)
            => throw new InvalidOperationException("The zero-value fast path must reject before copying values.");
    }

    public enum KernelOperation
    {
        Handle,
        ShouldHandle
    }

    public enum ThrowingWriterCallback
    {
        EventValueCount,
        ToSandboxValue,
        CopySandboxValues
    }
}
