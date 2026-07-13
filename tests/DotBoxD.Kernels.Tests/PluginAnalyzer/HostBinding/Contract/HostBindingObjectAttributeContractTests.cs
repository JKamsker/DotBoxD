using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding.Contract;

public sealed class HostBindingObjectAttributeContractTests
{
    [Fact]
    public void Constructor_exposes_binding_defaults()
    {
        const SandboxEffect effects = SandboxEffect.Cpu | SandboxEffect.HostStateRead;

        var attribute = new HostBindingObjectAttribute("host.player", "player.read", effects);

        Assert.Equal("host.player", attribute.BindingPrefix);
        Assert.Equal("player.read", attribute.DefaultCapability);
        Assert.Equal(effects, attribute.DefaultEffects);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_rejects_malformed_binding_prefix(string? bindingPrefix)
    {
        var exception = Record.Exception(() => new HostBindingObjectAttribute(
            bindingPrefix!,
            "player.read",
            SandboxEffect.Cpu | SandboxEffect.HostStateRead));

        Assert.Equal("bindingPrefix", Assert.IsAssignableFrom<ArgumentException>(exception).ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_rejects_malformed_default_capability(string? capability)
    {
        var exception = Record.Exception(() => new HostBindingObjectAttribute(
            "host.player",
            capability!,
            SandboxEffect.Cpu | SandboxEffect.HostStateRead));

        Assert.Equal("defaultCapability", Assert.IsAssignableFrom<ArgumentException>(exception).ParamName);
    }

    [Fact]
    public void Ignore_attribute_is_a_method_only_opt_out()
    {
        var usage = Assert.Single(typeof(HostBindingIgnoreAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>());

        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.Inherited);
    }
}
