using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.PluginLocal;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginHookSignatureTests
{
    [Fact]
    public async Task UseKernel_rejects_adapter_parameter_name_mismatch()
    {
        var server = PluginServer.Create();
        await server.InstallAsync(FireDamagePluginPackage.Create());

        var ex = Assert.Throws<SandboxValidationException>(
            () => server.Hooks.On(new MismatchedDamageEventAdapter()).UseKernel<FireDamageKernel>());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP033");
    }

    private sealed class MismatchedDamageEventAdapter : IPluginEventAdapter<DamageEvent>
    {
        public string EventName => "DamageEvent";

        public IReadOnlyList<Parameter> Parameters { get; } = [
            new("wrongDamageType", SandboxType.String),
            new("e_Amount", SandboxType.I32),
            new("e_TargetId", SandboxType.String)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageEvent e)
            => [
                SandboxValue.FromString(e.DamageType),
                SandboxValue.FromInt32(e.Amount),
                SandboxValue.FromString(e.TargetId)
            ];
    }
}
