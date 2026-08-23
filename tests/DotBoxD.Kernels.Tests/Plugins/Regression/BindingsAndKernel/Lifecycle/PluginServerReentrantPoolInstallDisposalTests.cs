using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Regression.BindingsAndKernel;

public sealed class PluginServerReentrantPoolInstallDisposalTests
{
    private const string BindingId = "host.test.reentrant.dispose";
    private const string CapabilityId = "test.reentrant.dispose";
    private static readonly SourceSpan Span = new(1, 1);

    [Fact]
    public async Task InstallPoolAsync_throws_when_grant_validation_disposes_server()
    {
        PluginServer? server = null;
        server = PluginServer.Create(
            configureHost: builder => builder.AddBinding(CreateDisposingBinding(() => server!.Dispose())),
            defaultPolicy: CreatePolicy());
        using (server)
        {
            var exception = await Assert.ThrowsAsync<ObjectDisposedException>(
                () => server.InstallPoolAsync(CreatePackage(), degreeOfParallelism: 1).AsTask());

            Assert.Equal(nameof(PluginServer), exception.ObjectName);
        }
    }

    private static BindingDescriptor CreateDisposingBinding(Action disposeServer)
        => new(
            BindingId,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu | SandboxEffect.HostStateWrite,
            CapabilityId,
            BindingCostModel.Fixed(1),
            AuditLevel.PerResource,
            BindingSafety.SideEffectingExternal,
            static (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: (_, _) => disposeServer());

    private static PluginPackage CreatePackage()
    {
        var module = new SandboxModule(
            "reentrant-dispose",
            SemVersion.One,
            SemVersion.One,
            [new CapabilityRequest(CapabilityId, "test")],
            [
                new SandboxFunction(
                    "ShouldHandle",
                    true,
                    [],
                    SandboxType.Bool,
                    [new ReturnStatement(new LiteralExpression(SandboxValue.FromBool(true), Span), Span)]),
                new SandboxFunction(
                    "Handle",
                    true,
                    [],
                    SandboxType.Unit,
                    [new ReturnStatement(new CallExpression(BindingId, [], null, Span), Span)]),
            ],
            new Dictionary<string, string> { ["pluginId"] = "reentrant-dispose", ["kernel"] = "ReentrantKernel" });
        var manifest = new PluginManifest(
            "reentrant-dispose",
            "IEventKernel<ReentrantEvent>",
            ExecutionMode.Interpreted,
            ["Cpu", "HostStateWrite"],
            [],
            [new HookSubscriptionManifest("ReentrantEvent", "ReentrantKernel")])
        {
            RequiredCapabilities = [CapabilityId]
        };

        return PluginPackage.Create(manifest, module, new KernelEntrypoints("ShouldHandle", "Handle"));
    }

    private static SandboxPolicy CreatePolicy()
        => SandboxPolicyBuilder.Create()
            .Grant(CapabilityId, new { }, SandboxEffect.HostStateWrite)
            .WithFuel(10_000)
            .WithMaxHostCalls(100)
            .WithWallTime(TimeSpan.FromSeconds(5))
            .Build();
}
