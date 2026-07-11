using System.Security.Cryptography;
using System.Text;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Serialization.Json;
using DotBoxD.Kernels.Verifier;
using DotBoxD.Kernels.Verifier.Generated;
using SharpFuzz;

var target = args.FirstOrDefault() ?? "json";
Fuzzer.Run(stream =>
{
    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    var bytes = buffer.ToArray();

    if (string.Equals(target, "json", StringComparison.Ordinal))
    {
        try
        {
            JsonImporter.Import(Encoding.UTF8.GetString(bytes));
        }
        catch (SandboxValidationException)
        {
        }

        return;
    }

    if (!string.Equals(target, "verifier", StringComparison.Ordinal))
    {
        throw new ArgumentException("Target must be 'json' or 'verifier'.", nameof(args));
    }

    var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    var manifest = new ArtifactManifest(
        1, "fuzz", "module", "plan", "policy", "bindings", "runtime", "compiler",
        "types", "effects", "verifier", "1.0.0", "net10.0", [], hash, DateTimeOffset.UnixEpoch);
    _ = new GeneratedAssemblyVerifier()
        .VerifyAsync(bytes, manifest, VerificationPolicy.BoxedValueDefaults(), CancellationToken.None)
        .AsTask().GetAwaiter().GetResult();
});
