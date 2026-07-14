using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding.Contract;

public sealed class HostBindingAttributeContractTests
{
    [Fact]
    public void Explicit_HostBindingAttribute_opts_into_receiver_forwarding()
    {
        var attribute = new HostBindingAttribute(
            "host.probe.read",
            "probe.read.value",
            SandboxEffect.Cpu | SandboxEffect.HostStateRead);

        Assert.False(attribute.IncludeReceiver);

        attribute.IncludeReceiver = true;

        Assert.True(attribute.IncludeReceiver);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Explicit_HostBindingAttribute_rejects_malformed_binding_id(string? bindingId)
    {
        AssertMalformedArgument(
            () => new HostBindingAttribute(
                bindingId!,
                "probe.read.value",
                SandboxEffect.Cpu | SandboxEffect.HostStateRead),
            "bindingId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Explicit_HostBindingAttribute_rejects_malformed_capability(string? capability)
    {
        AssertMalformedArgument(
            () => new HostBindingAttribute(
                "host.probe.read",
                capability!,
                SandboxEffect.Cpu | SandboxEffect.HostStateRead),
            "capability");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Auto_HostBindingAttribute_rejects_malformed_capability(string? capability)
    {
        AssertMalformedArgument(
            () => new HostBindingAttribute(
                capability!,
                SandboxEffect.Cpu | SandboxEffect.HostStateRead),
            "capability");
    }

    private static void AssertMalformedArgument(Action action, string paramName)
    {
        var exception = Record.Exception(action);
        var argumentException = Assert.IsAssignableFrom<ArgumentException>(exception);

        Assert.Equal(paramName, argumentException.ParamName);
    }
}
