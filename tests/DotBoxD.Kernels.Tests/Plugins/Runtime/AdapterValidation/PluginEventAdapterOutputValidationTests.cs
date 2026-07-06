using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Input;

namespace DotBoxD.Kernels.Tests.Plugins.Runtime.AdapterValidation;

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

    private sealed record NullValueListEvent(string TargetId);

    private sealed class NullValueListAdapter : IPluginEventAdapter<NullValueListEvent>
    {
        public string EventName => nameof(NullValueListEvent);

        public IReadOnlyList<Parameter> Parameters { get; } = [new("e_TargetId", SandboxType.String)];

        public IReadOnlyList<SandboxValue> ToSandboxValues(NullValueListEvent e) => null!;
    }
}
