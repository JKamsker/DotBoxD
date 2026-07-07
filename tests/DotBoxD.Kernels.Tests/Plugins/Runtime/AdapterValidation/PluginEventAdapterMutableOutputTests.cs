using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Runtime;

public sealed class PluginEventAdapterMutableOutputTests
{
    [Fact]
    public async Task HandleAsync_rejects_adapter_values_that_change_after_validation()
        => await AssertMutableOutputRejectedAsync(
            kernel => kernel.HandleAsync(
                new MutableOutputEventAdapter(),
                new MutableOutputEvent("player-1")).AsTask());

    [Fact]
    public async Task ShouldHandleAsync_rejects_adapter_values_that_change_after_validation()
        => await AssertMutableOutputRejectedAsync(
            kernel => kernel.ShouldHandleAsync(
                new MutableOutputEventAdapter(),
                new MutableOutputEvent("player-1")).AsTask());

    private static async Task AssertMutableOutputRejectedAsync(Func<InstalledKernel, Task> invoke)
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        var kernel = await server.InstallAsync(MutableInputPackage());

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(() => invoke(kernel));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK036");
        Assert.Empty(messages.Messages);
        Assert.Empty(kernel.ExecutionObservations);
    }

    private static PluginPackage MutableInputPackage()
    {
        var span = new SourceSpan(1, 1);
        var parameters = new Parameter[] { new("e_TargetId", SandboxType.String) };

        return PluginPackage.Create(
            new PluginManifest(
                "mutable-adapter-output",
                "IEventKernel<MutableOutputEvent>",
                ExecutionMode.Interpreted,
                ["Cpu", "Alloc", "HostStateWrite", "Concurrency", "Audit"],
                [],
                [new HookSubscriptionManifest(nameof(MutableOutputEvent), "MutableInputKernel")])
            {
                RequiredCapabilities = [RuntimeCapabilityIds.Async, PluginMessageBindings.CapabilityId]
            },
            new SandboxModule(
                "mutable-adapter-output",
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
                    ["pluginId"] = "mutable-adapter-output",
                    ["kernel"] = "MutableInputKernel"
                }));
    }

    private sealed record MutableOutputEvent(string TargetId);

    private sealed class MutableOutputEventAdapter : IPluginEventAdapter<MutableOutputEvent>
    {
        public string EventName => nameof(MutableOutputEvent);

        public IReadOnlyList<Parameter> Parameters { get; } = [new("e_TargetId", SandboxType.String)];

        public IReadOnlyList<SandboxValue> ToSandboxValues(MutableOutputEvent e)
            => new MutatingValues(e.TargetId);
    }

    private sealed class MutatingValues(string targetId) : IReadOnlyList<SandboxValue>
    {
        private int _reads;

        public int Count => 1;

        public SandboxValue this[int index]
        {
            get
            {
                if (index != 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                _reads++;
                return _reads == 1
                    ? SandboxValue.FromString(targetId)
                    : SandboxValue.FromInt32(42);
            }
        }

        public IEnumerator<SandboxValue> GetEnumerator()
            => throw new InvalidOperationException("The kernel should validate and consume adapter values by index.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
