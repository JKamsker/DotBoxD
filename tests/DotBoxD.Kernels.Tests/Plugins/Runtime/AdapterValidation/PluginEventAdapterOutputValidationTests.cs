using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Input;

namespace DotBoxD.Kernels.Tests.Plugins.Runtime;

public sealed class PluginEventAdapterOutputValidationTests
{
    [Fact]
    public async Task HandleAsync_rejects_null_adapter_value_list_before_execution()
    {
        var server = PluginAddendumTestPolicies.CreateServer(executionMode: ExecutionMode.Interpreted);
        var kernel = await server.InstallAsync(NullValueListPackage());

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await kernel.HandleAsync(
                new NullValueListAdapter(),
                new NullValueListEvent("player-1")).AsTask());

        Assert.Contains(ex.Diagnostics, diagnostic => diagnostic.Code == PluginEventAdapterShapeValidator.DiagnosticCode);
        Assert.Empty(kernel.ExecutionObservations);
        Assert.Null(kernel.LastExecution);
    }

    [Fact]
    public async Task HandleAsync_rejects_adapter_output_indexer_exceptions_as_validation()
    {
        var messages = new InMemoryPluginMessageSink();
        var kernel = await InstallKernelAsync(messages);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await kernel.HandleAsync(new ThrowingValueEventAdapter(), new ThrowingValueEvent("player-1")).AsTask());

        Assert.Contains(ex.Diagnostics, diagnostic => diagnostic.Code == PluginEventAdapterShapeValidator.DiagnosticCode);
        Assert.Empty(messages.Messages);
        Assert.Empty(kernel.ExecutionObservations);
    }

    [Fact]
    public async Task ShouldHandleAsync_rejects_adapter_output_indexer_exceptions_as_validation()
    {
        var messages = new InMemoryPluginMessageSink();
        var kernel = await InstallKernelAsync(messages);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await kernel.ShouldHandleAsync(new ThrowingValueEventAdapter(), new ThrowingValueEvent("player-1")).AsTask());

        Assert.Contains(ex.Diagnostics, diagnostic => diagnostic.Code == PluginEventAdapterShapeValidator.DiagnosticCode);
        Assert.Empty(messages.Messages);
        Assert.Empty(kernel.ExecutionObservations);
    }

    [Fact]
    public void CopyValidatedValues_rejects_null_adapter_value_list_as_validation()
    {
        var ex = Assert.Throws<SandboxValidationException>(
            () => PluginEventAdapterValueValidator.CopyValidatedValues(StringParameter, null!));

        Assert.Contains(ex.Diagnostics, diagnostic => diagnostic.Code == PluginEventAdapterShapeValidator.DiagnosticCode);
    }

    [Fact]
    public void CopyValidatedValues_preserves_adapter_value_cancellation()
        => Assert.Throws<OperationCanceledException>(
            () => PluginEventAdapterValueValidator.CopyValidatedValues(StringParameter, new CancellingValueList()));

    private static async Task<InstalledKernel> InstallKernelAsync(InMemoryPluginMessageSink messages)
    {
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        return await server.InstallAsync(ThrowingValuePackage());
    }

    private static Parameter[] StringParameter { get; } = [new("e_TargetId", SandboxType.String)];

    private static PluginPackage NullValueListPackage()
    {
        var span = new SourceSpan(1, 1);
        var parameters = new Parameter[] { new("e_TargetId", SandboxType.String) };

        return PluginPackage.Create(
            new PluginManifest(
                "adapter-null-output",
                "IEventKernel<NullValueListEvent>",
                ExecutionMode.Interpreted,
                ["Cpu"],
                [],
                [new HookSubscriptionManifest(nameof(NullValueListEvent), "NullValueListKernel")]),
            new SandboxModule(
                "adapter-null-output",
                SemVersion.One,
                SemVersion.One,
                [],
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
                        [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, span), span)])
                ],
                new Dictionary<string, string>
                {
                    ["pluginId"] = "adapter-null-output",
                    ["kernel"] = "NullValueListKernel"
                }));
    }

    private static PluginPackage ThrowingValuePackage()
    {
        var span = new SourceSpan(1, 1);
        var parameters = new Parameter[] { new("e_TargetId", SandboxType.String) };

        return PluginPackage.Create(
            new PluginManifest(
                "adapter-output-validation",
                "IEventKernel<ThrowingValueEvent>",
                ExecutionMode.Interpreted,
                ["Cpu", "Alloc", "HostStateWrite", "Concurrency", "Audit"],
                [],
                [new HookSubscriptionManifest(nameof(ThrowingValueEvent), "AdapterOutputKernel")])
            {
                RequiredCapabilities = [RuntimeCapabilityIds.Async, PluginMessageBindings.CapabilityId]
            },
            new SandboxModule(
                "adapter-output-validation",
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
                    ["pluginId"] = "adapter-output-validation",
                    ["kernel"] = "AdapterOutputKernel"
                }));
    }

    private sealed record NullValueListEvent(string TargetId);

    private sealed class NullValueListAdapter : IPluginEventAdapter<NullValueListEvent>
    {
        public string EventName => nameof(NullValueListEvent);

        public IReadOnlyList<Parameter> Parameters { get; } = [new("e_TargetId", SandboxType.String)];

        public IReadOnlyList<SandboxValue> ToSandboxValues(NullValueListEvent e) => null!;
    }

    private sealed record ThrowingValueEvent(string TargetId);

    private sealed class ThrowingValueEventAdapter : IPluginEventAdapter<ThrowingValueEvent>
    {
        public string EventName => nameof(ThrowingValueEvent);

        public IReadOnlyList<Parameter> Parameters { get; } = [new("e_TargetId", SandboxType.String)];

        public IReadOnlyList<SandboxValue> ToSandboxValues(ThrowingValueEvent e)
            => new ThrowingValueList();
    }

    private sealed class ThrowingValueList : IReadOnlyList<SandboxValue>
    {
        private static readonly SandboxValue[] Values = [SandboxValue.FromString("unused")];

        public int Count => 1;

        public SandboxValue this[int index]
            => throw new InvalidOperationException("Adapter value indexer failed.");

        public IEnumerator<SandboxValue> GetEnumerator()
            => ((IEnumerable<SandboxValue>)Values).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }

    private sealed class CancellingValueList : IReadOnlyList<SandboxValue>
    {
        public int Count => throw new OperationCanceledException();

        public SandboxValue this[int index] => throw new OperationCanceledException();

        public IEnumerator<SandboxValue> GetEnumerator()
            => throw new OperationCanceledException();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
