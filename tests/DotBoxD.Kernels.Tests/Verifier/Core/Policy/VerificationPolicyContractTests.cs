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
    public void IsMemberAllowed_rejects_null_member_signature_at_public_boundary()
    {
        var policy = VerificationPolicy.BoxedValueDefaults();

        var exception = Assert.Throws<ArgumentNullException>(
            () => policy.IsMemberAllowed(null!));

        Assert.Equal("memberSignature", exception.ParamName);
        Assert.False(policy.IsMemberAllowed("DotBoxD.Does.Not.Exist::Missing()"));
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

    [Fact]
    public void VerifierVersion_rejects_null_at_public_property_boundary()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => _ = VerificationPolicy.BoxedValueDefaults() with
            {
                VerifierVersion = null!
            });

        Assert.Equal("VerifierVersion", exception.ParamName);
    }

    [Fact]
    public void VerifierVersion_constructor_rejects_null_at_public_boundary()
    {
        var policy = VerificationPolicy.BoxedValueDefaults();

        var exception = Assert.Throws<ArgumentNullException>(
            () => _ = new VerificationPolicy(
                policy.AllowedAssemblies,
                policy.AllowedAssemblyIdentities,
                policy.AllowedTypes,
                policy.AllowedMembers,
                policy.ForbiddenTypePrefixes,
                policy.RuntimeFacadeIdentities,
                VerifierVersion: null!));

        Assert.Equal("VerifierVersion", exception.ParamName);
    }

    [Fact]
    public void VerifierVersion_rejects_whitespace_at_public_property_boundary()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => _ = VerificationPolicy.BoxedValueDefaults() with
            {
                VerifierVersion = "   "
            });

        Assert.Equal("VerifierVersion", exception.ParamName);
    }
}
