namespace SafeIR.Verifier;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

public sealed class GeneratedAssemblyVerifier : IGeneratedAssemblyVerifier
{
    public ValueTask<VerificationResult> VerifyAsync(
        ReadOnlyMemory<byte> assemblyBytes,
        ArtifactManifest manifest,
        VerificationPolicy policy,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<VerificationDiagnostic>();
        var assemblyHash = Convert.ToHexString(SHA256.HashData(assemblyBytes.Span)).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(manifest.AssemblyHash) &&
            !StringComparer.Ordinal.Equals(manifest.AssemblyHash, assemblyHash)) {
            diagnostics.Add(new VerificationDiagnostic("V-MANIFEST-HASH", "assembly hash does not match manifest"));
        }

        try {
            using var stream = new MemoryStream(assemblyBytes.ToArray(), writable: false);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata) {
                diagnostics.Add(new VerificationDiagnostic("V-PE-METADATA", "assembly has no CLR metadata"));
            }
            else {
                VerifyMetadata(peReader, peReader.GetMetadataReader(), policy, diagnostics, cancellationToken);
            }
        }
        catch (BadImageFormatException ex) {
            diagnostics.Add(new VerificationDiagnostic("V-PE-FORMAT", ex.Message));
        }

        return ValueTask.FromResult(new VerificationResult(
            diagnostics.Count == 0,
            diagnostics,
            assemblyHash,
            policy.VerifierVersion,
            DateTimeOffset.UtcNow));
    }

    private static void VerifyMetadata(
        PEReader peReader,
        MetadataReader reader,
        VerificationPolicy policy,
        List<VerificationDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        VerifyAssemblyReferences(reader, policy, diagnostics);
        VerifyTypeReferences(reader, policy, diagnostics);
        VerifyDefinitions(peReader, reader, policy, diagnostics, cancellationToken);
        if (reader.ManifestResources.Count > 0) {
            diagnostics.Add(new VerificationDiagnostic("V-RESOURCE", "embedded resources are not allowed"));
        }

        if (reader.GetTableRowCount(TableIndex.ImplMap) > 0) {
            diagnostics.Add(new VerificationDiagnostic("V-PINVOKE", "P/Invoke metadata is not allowed"));
        }
    }

    private static void VerifyAssemblyReferences(
        MetadataReader reader,
        VerificationPolicy policy,
        List<VerificationDiagnostic> diagnostics)
    {
        foreach (var handle in reader.AssemblyReferences) {
            var reference = reader.GetAssemblyReference(handle);
            var name = reader.GetString(reference.Name);
            if (!policy.AllowedAssemblies.Contains(name)) {
                diagnostics.Add(new VerificationDiagnostic("V-ASM-REF", $"assembly reference '{name}' is not allowed"));
            }
        }
    }

    private static void VerifyTypeReferences(
        MetadataReader reader,
        VerificationPolicy policy,
        List<VerificationDiagnostic> diagnostics)
    {
        foreach (var handle in reader.TypeReferences) {
            var name = MetadataName.TypeReference(reader, handle);
            if (policy.ForbiddenTypePrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal))) {
                diagnostics.Add(new VerificationDiagnostic("V-TYPE-FORBIDDEN", $"type reference '{name}' is forbidden"));
            }
            else if (!policy.AllowedTypes.Contains(name)) {
                diagnostics.Add(new VerificationDiagnostic("V-TYPE-REF", $"type reference '{name}' is not allowed"));
            }
        }
    }

    private static void VerifyDefinitions(
        PEReader peReader,
        MetadataReader reader,
        VerificationPolicy policy,
        List<VerificationDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        foreach (var typeHandle in reader.TypeDefinitions) {
            cancellationToken.ThrowIfCancellationRequested();
            var type = reader.GetTypeDefinition(typeHandle);
            VerifyFields(reader, type, diagnostics);
            VerifyMethods(peReader, reader, policy, type, diagnostics);
        }
    }

    private static void VerifyFields(MetadataReader reader, TypeDefinition type, List<VerificationDiagnostic> diagnostics)
    {
        foreach (var fieldHandle in type.GetFields()) {
            var field = reader.GetFieldDefinition(fieldHandle);
            if ((field.Attributes & FieldAttributes.Static) != 0 &&
                (field.Attributes & (FieldAttributes.InitOnly | FieldAttributes.Literal)) == 0) {
                diagnostics.Add(new VerificationDiagnostic("V-FIELD-STATIC", "mutable static fields are not allowed"));
            }
        }
    }

    private static void VerifyMethods(
        PEReader peReader,
        MetadataReader reader,
        VerificationPolicy policy,
        TypeDefinition type,
        List<VerificationDiagnostic> diagnostics)
    {
        foreach (var methodHandle in type.GetMethods()) {
            var method = reader.GetMethodDefinition(methodHandle);
            var name = reader.GetString(method.Name);
            if (name is ".cctor" or "Finalize") {
                diagnostics.Add(new VerificationDiagnostic("V-METHOD-SPECIAL", $"method '{name}' is not allowed"));
            }

            if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0) {
                diagnostics.Add(new VerificationDiagnostic("V-METHOD-PINVOKE", $"method '{name}' has P/Invoke attributes"));
            }

            if (method.RelativeVirtualAddress != 0) {
                var body = peReader.GetMethodBody(method.RelativeVirtualAddress);
                OpCodeVerifier.VerifyBody(reader, policy, body, diagnostics);
            }
        }
    }
}
