namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class PolymorphicHandleAttributeContractTests
{
    [Theory]
    [InlineData("Id")]
    [InlineData("_id")]
    [InlineData("Id2")]
    public void Polymorphic_handle_attribute_accepts_member_names(string keyMember)
    {
        var attribute = new PolymorphicHandleAttribute(keyMember);

        Assert.Equal(keyMember, attribute.KeyMember);
    }

    [Theory]
    [InlineData(null, typeof(ArgumentNullException))]
    [InlineData("", typeof(ArgumentException))]
    [InlineData("   ", typeof(ArgumentException))]
    public void Polymorphic_handle_attribute_rejects_missing_key_member(
        string? keyMember,
        Type exceptionType)
    {
        var exception = Assert.Throws(
            exceptionType,
            () => new PolymorphicHandleAttribute(keyMember!));

        var argumentException = Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Equal("keyMember", argumentException.ParamName);
    }

    [Theory]
    [InlineData("id.bad")]
    [InlineData("id bad")]
    [InlineData("id\u0001")]
    public void Polymorphic_handle_attribute_rejects_malformed_key_member_metadata(
        string keyMember)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new PolymorphicHandleAttribute(keyMember));

        Assert.Equal("keyMember", exception.ParamName);
    }
}
