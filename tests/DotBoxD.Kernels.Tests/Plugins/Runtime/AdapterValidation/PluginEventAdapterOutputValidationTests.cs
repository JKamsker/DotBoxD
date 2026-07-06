using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Runtime;

public sealed class PluginEventAdapterOutputValidationTests
{
    [Fact]
    public async Task HandleAsync_rejects_adapter_output_indexer_exceptions_as_validation()
    {
        var messages = new InMemoryPluginMessageSink();
        var kernel = await InstallKernelAsync(messages);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await kernel.HandleAsync(new ThrowingValueEventAdapter(), new ThrowingValueEvent("player-1")).AsTask());

        Assert.Contains(ex.Diagnostics, diagnostic => diagnostic.Code == "DBXK036");
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

        Assert.Contains(ex.Diagnostics, diagnostic => diagnostic.Code == "DBXK036");
        Assert.Empty(messages.Messages);
        Assert.Empty(kernel.ExecutionObservations);
    }

    private static async Task<InstalledKernel> InstallKernelAsync(InMemoryPluginMessageSink messages)
    {
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        return await server.InstallAsync(ThrowingValuePackage());
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
        public int Count => 1;

        public SandboxValue this[int index]
            => throw new InvalidOperationException("Adapter value indexer failed.");

        public IEnumerator<SandboxValue> GetEnumerator()
            => throw new InvalidOperationException("Adapter value output should be validated by index.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
