using System.Security.Cryptography;
using DotBoxD.Plugins.Packaging;

namespace DotBoxD.Kernels.Tests.Plugins.Packaging;

public sealed class PluginArtifactTests
{
    [Fact]
    public void Signature_authenticates_payload_and_every_identity_field()
    {
        using var key = RSA.Create(2048);
        var policy = Trust(key);
        var artifact = PluginArtifactSigning.Sign(FireDamagePluginPackage.Create(), "1.0.0", "example.dev", key);
        var verified = policy.Verify(PluginArtifact.Deserialize(artifact.Serialize()));
        Assert.True(verified.IsSigned);
        Assert.Equal(artifact.PluginId, verified.Package.Manifest.PluginId);
        foreach (var tampered in new[]
        {
            artifact with { PluginId = "other" }, artifact with { Version = "2.0.0" },
            artifact with { Publisher = "other.dev" }, artifact with { PackageJson = artifact.PackageJson + " " },
            artifact with { SchemaVersion = 2 }, artifact with { Signature = "broken" },
            artifact with { ContentHash = new string('0', 64) }, artifact with { KeyId = "other" }
        })
        {
            Assert.Throws<InvalidDataException>(() => policy.Verify(tampered));
        }
    }

    [Fact]
    public void External_signer_can_handwrite_the_identical_artifact()
    {
        using var key = RSA.Create(2048);
        var artifact = PluginArtifactSigning.CreateUnsigned(FireDamagePluginPackage.Create(), "1.0.0", "example.dev")
            with
        { KeyId = PluginArtifactSigning.GetKeyId(key) };
        var signature = key.SignData(PluginArtifactSigning.GetSigningBytes(artifact), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        Assert.True(Trust(key).Verify(artifact with { Signature = Convert.ToBase64String(signature) }).IsSigned);
    }

    [Fact]
    public void Unsigned_requires_explicit_policy_and_invalid_signatures_never_downgrade()
    {
        var artifact = PluginArtifactSigning.CreateUnsigned(FireDamagePluginPackage.Create(), "1.0.0", "example.dev");
        Assert.Throws<InvalidDataException>(() => new PluginTrustPolicy([]).Verify(artifact));
        var permissive = new PluginTrustPolicy([], allowUnsigned: true);
        Assert.False(permissive.Verify(artifact).IsSigned);
        Assert.Throws<InvalidDataException>(() => permissive.Verify(artifact with { KeyId = "unknown", Signature = "" }));
        using var key = RSA.Create(2048);
        var signed = PluginArtifactSigning.Sign(FireDamagePluginPackage.Create(), "1.0.0", "example.dev", key);
        Assert.Throws<InvalidDataException>(() => permissive.Verify(signed));
    }

    [Fact]
    public void Upgrade_diff_is_sorted_and_rejects_different_plugin_identities()
    {
        var manifest = FireDamagePluginPackage.Create().Manifest with { RequiredCapabilities = ["read", "old"] };
        var next = manifest with { RequiredCapabilities = ["write", "read", "write"] };
        var diff = PluginCapabilityChanges.Compare(manifest, next);
        Assert.Equal(["write"], diff.Added);
        Assert.Equal(["old"], diff.Removed);
        Assert.Equal(["read"], diff.Unchanged);
        Assert.True(diff.RequiresAdditionalPermission);
        Assert.Throws<ArgumentException>(() => PluginCapabilityChanges.Compare(manifest, next with { PluginId = "other" }));
    }

    [Fact]
    public void Duplicate_artifact_fields_are_rejected_before_trust_evaluation()
    {
        var artifact = PluginArtifactSigning.CreateUnsigned(FireDamagePluginPackage.Create(), "1.0.0", "example.dev");
        var json = artifact.Serialize();
        Assert.Throws<System.Text.Json.JsonException>(() => PluginArtifact.Deserialize(json[..^1] + ",\"SchemaVersion\":1}"));
    }

    private static PluginTrustPolicy Trust(RSA key) => new(
        [new PluginPublisherKey("example.dev", Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()))]);
}
