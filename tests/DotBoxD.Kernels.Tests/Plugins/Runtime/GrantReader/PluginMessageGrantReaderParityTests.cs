using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Runtime.GrantReader;

public sealed class PluginMessageGrantReaderParityTests
{
    [Theory]
    [InlineData("allowedTargets", "target-1,", "target-1")]
    [InlineData("targetPrefixes", "target:,", "target:1")]
    public async Task Direct_host_message_grant_reader_rejects_trailing_empty_csv_targets(
        string grantParameter,
        string grantValue,
        string targetId)
    {
        var sink = new InMemoryPluginMessageSink();
        var binding = PluginMessageBindings.CreateSend(sink);
        var audit = new InMemoryAuditSink();
        var context = MessageContext(binding, audit, grantParameter, grantValue);

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(
            async () => await binding.Invoke(
                    context,
                    [SandboxValue.FromString(targetId), SandboxValue.FromString("hello")],
                    CancellationToken.None)
                .AsTask());

        Assert.Equal(SandboxErrorCode.PermissionDenied, ex.Error.Code);
        Assert.Empty(sink.Messages);
        Assert.DoesNotContain(audit.Events, e => e.Kind == "PluginMessage");
    }

    private static SandboxContext MessageContext(
        BindingDescriptor binding,
        InMemoryAuditSink audit,
        string grantParameter,
        string grantValue)
    {
        var policy = new SandboxPolicy(
            "plugin-message-grant-reader-parity",
            SandboxEffect.HostStateWrite | SandboxEffect.Audit,
            [
                new CapabilityGrant(
                    PluginMessageBindings.CapabilityId,
                    new Dictionary<string, string>
                    {
                        [grantParameter] = grantValue
                    })
            ],
            new ResourceLimits(MaxFuel: 10_000));

        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistry([binding]),
            audit,
            CancellationToken.None,
            moduleHash: "module",
            policyHash: "policy");
    }
}
