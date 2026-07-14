namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class HandleSubtypeAttributeContractTests
{
    [Theory]
    [InlineData("id.bad")]
    [InlineData("player bad id")]
    [InlineData("player\u0001")]
    public void Handle_subtype_attribute_rejects_malformed_discriminator_metadata(
        string discriminator)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new HandleSubtypeAttribute(
                typeof(HandleSubtypeAttributeContractTests),
                discriminator,
                "combatant.player",
                "combatant.player.read"));

        Assert.Equal("discriminator", exception.ParamName);
    }
}
