using System.Security.Cryptography;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Plugins.Packaging;

/// <summary>A host-approved publisher and its base64 SubjectPublicKeyInfo RSA public key.</summary>
public sealed record PluginPublisherKey(string Publisher, string SubjectPublicKeyInfo);

/// <summary>Verified provenance. Trust verification does not replace IR validation or capability authorization.</summary>
public sealed record VerifiedPluginArtifact(PluginArtifact Artifact, PluginPackage Package, bool IsSigned);

/// <summary>Explicit host trust policy; unsigned artifacts are rejected by default.</summary>
public sealed class PluginTrustPolicy
{
    private readonly Dictionary<(string Publisher, string KeyId), string> _keys = [];
    public bool AllowUnsigned { get; }

    public PluginTrustPolicy(IEnumerable<PluginPublisherKey> approvedKeys, bool allowUnsigned = false)
    {
        ArgumentNullException.ThrowIfNull(approvedKeys);
        foreach (var approved in approvedKeys)
        {
            ArgumentNullException.ThrowIfNull(approved);
            ArgumentException.ThrowIfNullOrWhiteSpace(approved.Publisher);
            using var key = RSA.Create();
            var bytes = Convert.FromBase64String(approved.SubjectPublicKeyInfo);
            key.ImportSubjectPublicKeyInfo(bytes, out var consumed);
            if (consumed != bytes.Length || key.KeySize < 2048)
            {
                throw new ArgumentException("Expected a complete RSA public key of at least 2048 bits.", nameof(approvedKeys));
            }
            _keys.Add((approved.Publisher, PluginArtifactSigning.GetKeyId(key)), approved.SubjectPublicKeyInfo);
        }
        AllowUnsigned = allowUnsigned;
    }

    /// <summary>Checks trust before parsing the package; rejects any signature failure even when unsigned is allowed.</summary>
    public VerifiedPluginArtifact Verify(PluginArtifact artifact)
    {
        PluginArtifactSigning.Validate(artifact);
        var signed = artifact.Signature is not null || artifact.KeyId is not null;
        if (!signed)
        {
            if (!AllowUnsigned)
            {
                throw new InvalidDataException("Host policy rejects unsigned plugin artifacts.");
            }
        }
        else
        {
            if (artifact.Signature is null || artifact.KeyId is null ||
                !_keys.TryGetValue((artifact.Publisher, artifact.KeyId), out var encodedKey))
            {
                throw new InvalidDataException("Plugin publisher/key is not approved by the host.");
            }
            using var key = RSA.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(encodedKey), out _);
            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(artifact.Signature);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException("Malformed plugin signature.", exception);
            }
            if (!key.VerifyData(PluginArtifactSigning.GetSigningBytes(artifact), signature,
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            {
                throw new InvalidDataException("Invalid plugin signature.");
            }
        }
        var package = PluginPackageJsonSerializer.Import(artifact.PackageJson);
        if (!string.Equals(package.Manifest.PluginId, artifact.PluginId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Signed plugin identity does not match the package manifest.");
        }
        return new VerifiedPluginArtifact(artifact, package, signed);
    }
}
