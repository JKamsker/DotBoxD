using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Tests.Plugins.Regression.BindingsAndKernel;

public sealed class RevokedKernelObservationTerminalTests
{
    private const string BindingId = "test.revokeInstalledKernel";

    [Fact]
    public async Task Handle_observation_matches_policy_denial_when_kernel_is_revoked_during_execution()
    {
        InstalledKernel? installed = null;
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: builder => builder.AddBinding(RevokeInstalledKernelBinding(() => installed!)),
            defaultPolicy: Policy());
        installed = await server.InstallAsync(Package());

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(
            async () => await installed.HandleAsync(
                RevokeEventAdapter.Instance,
                new RevokeEvent("player-1")).AsTask());

        Assert.Equal(SandboxErrorCode.PolicyDenied, ex.Error.Code);
        Assert.True(installed.IsRevoked);
        var observation = Assert.Single(installed.ExecutionObservations);
        Assert.Equal("Handle", observation.Entrypoint);
        Assert.False(observation.Succeeded);
        // The recorded Handle terminal result must match HandleAsync's PolicyDenied failure,
        // not the cancellation observed by the lower-level prepared execution.
        Assert.Equal(SandboxErrorCode.PolicyDenied, observation.ErrorCode);
    }

    private static BindingDescriptor RevokeInstalledKernelBinding(Func<InstalledKernel> kernel)
        => new(
            BindingId,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) =>
            {
                kernel().Revoke();
                return ValueTask.FromResult(SandboxValue.Unit);
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static SandboxPolicy Policy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private static PluginPackage Package()
    {
        var span = new SourceSpan(1, 1);
        return PluginPackage.Create(
            new PluginManifest(
                "revocation-observation",
                "IEventKernel<RevokeEvent>",
                ExecutionMode.Interpreted,
                ["Cpu"],
                [],
                [new HookSubscriptionManifest(nameof(RevokeEvent), "RevokingKernel")]),
            new SandboxModule(
                "revocation-observation",
                SemVersion.One,
                SemVersion.One,
                [],
                [ShouldHandle(span), Handle(span)],
                new Dictionary<string, string>
                {
                    ["pluginId"] = "revocation-observation",
                    ["kernel"] = "RevokingKernel"
                }));
    }

    private static SandboxFunction ShouldHandle(SourceSpan span)
        => new(
            "ShouldHandle",
            true,
            [new Parameter("targetId", SandboxType.String)],
            SandboxType.Bool,
            [new ReturnStatement(new LiteralExpression(SandboxValue.FromBool(true), span), span)]);

    private static SandboxFunction Handle(SourceSpan span)
        => new(
            "Handle",
            true,
            [new Parameter("targetId", SandboxType.String)],
            SandboxType.Unit,
            [new ReturnStatement(new CallExpression(BindingId, [], null, span), span)]);

    private sealed record RevokeEvent(string TargetId);

    private sealed class RevokeEventAdapter : IPluginEventAdapter<RevokeEvent>
    {
        public static RevokeEventAdapter Instance { get; } = new();
        public string EventName => nameof(RevokeEvent);
        public IReadOnlyList<Parameter> Parameters { get; } = [new("targetId", SandboxType.String)];
        public IReadOnlyList<SandboxValue> ToSandboxValues(RevokeEvent e)
            => [SandboxValue.FromString(e.TargetId)];
    }
}
