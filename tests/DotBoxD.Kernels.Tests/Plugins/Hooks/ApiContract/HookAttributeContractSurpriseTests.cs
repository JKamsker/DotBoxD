namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class HookAttributeContractSurpriseTests
{
    private readonly record struct HookResult(bool Success, string? Reason) : IHookResult;

    [Theory]
    [InlineData("combat.damage")]
    [InlineData("combat_damage.damage2")]
    public void Hook_attribute_accepts_dotted_hook_names(string name)
    {
        var attribute = new HookAttribute(name, typeof(HookResult));

        Assert.Equal(name, attribute.Name);
        Assert.Equal(typeof(HookResult), attribute.ResultType);
    }

    [Theory]
    [InlineData("bad..hook")]
    [InlineData("bad hook")]
    [InlineData("bad\u0001hook")]
    [InlineData(".bad")]
    [InlineData("bad.")]
    public void Hook_attribute_rejects_malformed_hook_names(string name)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new HookAttribute(name, typeof(HookResult)));

        Assert.Equal("name", exception.ParamName);
    }
}
