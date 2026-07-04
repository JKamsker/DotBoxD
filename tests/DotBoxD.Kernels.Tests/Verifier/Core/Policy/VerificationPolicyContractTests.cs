using DotBoxD.Kernels.Verifier;

namespace DotBoxD.Kernels.Tests.Verifier.Core.Policy;

public sealed class VerificationPolicyContractTests
{
    [Fact]
    public void WithExpectedManifest_rejects_null_identity_at_public_boundary()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => VerificationPolicy.BoxedValueDefaults().WithExpectedManifest(null!));

        Assert.Equal("identity", exception.ParamName);
    }

    [Fact]
    public void AllowedMembers_rejects_null_entries_at_public_property_boundary()
    {
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => _ = VerificationPolicy.BoxedValueDefaults() with
            {
                AllowedMembers = new HashSet<string?> { null }!
            });

        Assert.Equal("AllowedMembers", exception.ParamName);
    }

    [Fact]
    public void ForbiddenTypePrefixes_rejects_null_entries_at_public_property_boundary()
    {
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => _ = VerificationPolicy.BoxedValueDefaults() with
            {
                ForbiddenTypePrefixes = new HashSet<string?> { null }!
            });

        Assert.Equal("ForbiddenTypePrefixes", exception.ParamName);
    }
}
