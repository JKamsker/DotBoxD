using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Plugins.Packaging;

/// <summary>Optional RSA-PSS/SHA-256 package signing over public package serialization primitives.</summary>
public static class PluginArtifactSigning
{
    public static PluginArtifact CreateUnsigned(PluginPackage package, string version, string publisher)
    {
        ArgumentNullException.ThrowIfNull(package);
        var json = PluginPackageJsonSerializer.Export(package);
        var artifact = new PluginArtifact(PluginArtifact.CurrentSchemaVersion, package.Manifest.PluginId,
            version, publisher, json, Hash(json), null, null);
        Validate(artifact);
        return artifact;
    }

    public static PluginArtifact Sign(PluginPackage package, string version, string publisher, RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        if (privateKey.KeySize < 2048)
        {
            throw new ArgumentException("RSA signing keys must have at least 2048 bits.", nameof(privateKey));
        }
        var artifact = CreateUnsigned(package, version, publisher) with { KeyId = GetKeyId(privateKey) };
        return artifact with
        {
            Signature = Convert.ToBase64String(privateKey.SignData(GetSigningBytes(artifact),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
        };
    }

    /// <summary>Stable SHA-256 identity of the SubjectPublicKeyInfo bytes.</summary>
    public static string GetKeyId(RSA key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Convert.ToHexStringLower(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
    }

    /// <summary>Canonical signing payload, exposed so consumers can use external signing services.</summary>
    public static byte[] GetSigningBytes(PluginArtifact artifact)
    {
        Validate(artifact);
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartArray();
        writer.WriteStringValue("DotBoxD.PluginArtifact.RSA-PSS-SHA256");
        writer.WriteNumberValue(artifact.SchemaVersion);
        writer.WriteStringValue(artifact.PluginId);
        writer.WriteStringValue(artifact.Version);
        writer.WriteStringValue(artifact.Publisher);
        writer.WriteStringValue(artifact.ContentHash);
        writer.WriteStringValue(artifact.KeyId);
        writer.WriteStringValue(artifact.PackageJson);
        writer.WriteEndArray();
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    internal static string Hash(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    internal static void Validate(PluginArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.SchemaVersion != PluginArtifact.CurrentSchemaVersion)
        {
            throw new InvalidDataException("Unsupported plugin artifact schema version.");
        }
        ValidateIdentity(artifact.PluginId);
        ValidateIdentity(artifact.Version);
        ValidateIdentity(artifact.Publisher);
        if (artifact.PackageJson is null || artifact.PackageJson.Length > PluginArtifact.MaximumJsonLength ||
            !string.Equals(artifact.ContentHash, Hash(artifact.PackageJson), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Plugin content hash does not match its payload.");
        }
    }

    private static void ValidateIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl))
        {
            throw new InvalidDataException("Plugin identity fields must be nonempty, bounded text without control characters.");
        }
    }
}
