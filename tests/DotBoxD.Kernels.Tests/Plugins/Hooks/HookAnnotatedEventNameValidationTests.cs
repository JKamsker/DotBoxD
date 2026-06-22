using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class HookAnnotatedEventNameValidationTests
{
    [Hook("annotated.ordinary", typeof(AnnotatedResult))]
    private sealed record AnnotatedOrdinaryEvent(int Value, string TargetId);

    private readonly record struct AnnotatedResult(bool Success, string? Reason) : IHookResult;

    [Fact]
    public async Task Ordinary_kernel_validation_accepts_clr_event_name_for_hook_annotated_context()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        var kernel = await server.InstallAsync(OrdinaryPackage());

        server.Hooks.On<AnnotatedOrdinaryEvent>().Use(kernel);
        await server.Hooks.PublishAsync(new AnnotatedOrdinaryEvent(10, "target"));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("target", message.TargetId);
        Assert.Equal("ordinary matched", message.Message);
    }

    private static PluginPackage OrdinaryPackage()
    {
        var span = new SourceSpan(1, 1);
        var parameters = new Parameter[] {
            new("e_Value", SandboxType.I32),
            new("e_TargetId", SandboxType.String)
        };

        return PluginPackage.Create(
            new PluginManifest(
                "annotated-ordinary-kernel",
                "IEventKernel<AnnotatedOrdinaryEvent>",
                ExecutionMode.Interpreted,
                ["Cpu", "Alloc", "HostStateWrite", "Concurrency", "Audit"],
                [],
                [
                    new HookSubscriptionManifest(
                        nameof(AnnotatedOrdinaryEvent),
                        "AnnotatedOrdinaryKernel")
                ])
            {
                RequiredCapabilities = [RuntimeCapabilityIds.Async, PluginMessageBindings.CapabilityId]
            },
            new SandboxModule(
                "annotated-ordinary-kernel",
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
                                        new LiteralExpression(SandboxValue.FromString("ordinary matched"), span)
                                    ],
                                    null,
                                    span),
                                span)
                        ])
                ],
                new Dictionary<string, string>
                {
                    ["pluginId"] = "annotated-ordinary-kernel",
                    ["kernel"] = "AnnotatedOrdinaryKernel"
                }));
    }
}
