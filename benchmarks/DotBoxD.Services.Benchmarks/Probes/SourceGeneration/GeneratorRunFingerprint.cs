using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.Benchmarks.Probes;

internal sealed record GeneratorRunFingerprint(
    string SourceHash,
    int SourceCount,
    string DiagnosticHash,
    int DiagnosticCount)
{
    public static GeneratorRunFingerprint Create(GeneratorDriverRunResult result)
    {
        foreach (var generator in result.Results)
        {
            if (generator.Exception is not null)
            {
                throw new InvalidOperationException("The source generator failed.", generator.Exception);
            }
        }

        var sources = result.Results
            .SelectMany(static generator => generator.GeneratedSources)
            .OrderBy(static source => source.HintName, StringComparer.Ordinal)
            .ToArray();
        using var sourceHash = new LengthPrefixedHash("DotBoxD.Services.SourceGenerator.Sources/v1");
        sourceHash.Append(sources.Length);
        foreach (var source in sources)
        {
            sourceHash.Append(source.HintName);
            sourceHash.Append(source.SourceText.ToString());
        }

        var diagnostics = result.Diagnostics
            .Select(HashDiagnostic)
            .OrderBy(static hash => hash, StringComparer.Ordinal)
            .ToArray();
        using var diagnosticHash = new LengthPrefixedHash("DotBoxD.Services.SourceGenerator.Diagnostics/v1");
        diagnosticHash.Append(diagnostics.Length);
        foreach (var diagnostic in diagnostics)
        {
            diagnosticHash.Append(diagnostic);
        }

        return new GeneratorRunFingerprint(
            sourceHash.Finish(),
            sources.Length,
            diagnosticHash.Finish(),
            diagnostics.Length);
    }

    public static string HashText(string domain, string value)
    {
        using var hash = new LengthPrefixedHash(domain);
        hash.Append(value);
        return hash.Finish();
    }

    public static string HashLines(string domain, IEnumerable<string> lines)
    {
        using var hash = new LengthPrefixedHash(domain);
        foreach (var line in lines)
        {
            hash.Append(line);
        }

        return hash.Finish();
    }

    private static void AppendDiagnostic(LengthPrefixedHash hash, Diagnostic diagnostic)
    {
        hash.Append(diagnostic.Id);
        hash.Append((int)diagnostic.Severity);
        hash.Append(diagnostic.WarningLevel);
        hash.Append(diagnostic.IsSuppressed);
        hash.Append(diagnostic.GetMessage(CultureInfo.InvariantCulture));
        AppendLocation(hash, diagnostic.Location);
        hash.Append(diagnostic.AdditionalLocations.Count);
        foreach (var location in diagnostic.AdditionalLocations)
        {
            AppendLocation(hash, location);
        }

        var properties = diagnostic.Properties.OrderBy(static property => property.Key, StringComparer.Ordinal);
        hash.Append(diagnostic.Properties.Count);
        foreach (var property in properties)
        {
            hash.Append(property.Key);
            hash.Append(property.Value ?? string.Empty);
        }
    }

    private static string HashDiagnostic(Diagnostic diagnostic)
    {
        using var hash = new LengthPrefixedHash("DotBoxD.Services.SourceGenerator.Diagnostic/v1");
        AppendDiagnostic(hash, diagnostic);
        return hash.Finish();
    }

    private static void AppendLocation(LengthPrefixedHash hash, Location location)
    {
        hash.Append((int)location.Kind);
        hash.Append(location.SourceTree?.FilePath ?? string.Empty);
        hash.Append(location.SourceSpan.Start);
        hash.Append(location.SourceSpan.Length);
        AppendLineSpan(hash, location.GetLineSpan());
        AppendLineSpan(hash, location.GetMappedLineSpan());
    }

    private static void AppendLineSpan(LengthPrefixedHash hash, FileLinePositionSpan span)
    {
        hash.Append(span.Path);
        hash.Append(span.StartLinePosition.Line);
        hash.Append(span.StartLinePosition.Character);
        hash.Append(span.EndLinePosition.Line);
        hash.Append(span.EndLinePosition.Character);
        hash.Append(span.HasMappedPath);
    }
}

internal static class GeneratorTrackedReasonFingerprint
{
    private static readonly string[] StepNames =
    [
        "ExistingTypeDeclarations",
        "ExistingTypeKeys",
        "ExistingTypes",
        "ExistingTypeLocations",
        "ExistingTypeValidatedServiceResults",
        "ExistingTypeDiagnostics",
        "FinalRejectionInputs",
        "FinalRejectedServices",
        "ServiceResults",
        "Services",
        "ServiceBundles",
        "AllServices",
        "AllServiceMetadata",
    ];

    public static string Create(GeneratorDriverRunResult result)
    {
        var generator = result.Results.Single();
        var entries = new List<string>(StepNames.Length + 1);
        foreach (var name in StepNames)
        {
            entries.Add(generator.TrackedSteps.TryGetValue(name, out var steps)
                ? name + "=" + Summarize(steps.SelectMany(static step => step.Outputs).Select(static output => output.Reason))
                : name + "=<absent>");
        }

        var outputReasons = generator.TrackedOutputSteps.Values
            .SelectMany(static steps => steps)
            .SelectMany(static step => step.Outputs)
            .Select(static output => output.Reason);
        entries.Add("SourceOutputs=" + Summarize(outputReasons));
        return string.Join(';', entries);
    }

    private static string Summarize(IEnumerable<IncrementalStepRunReason> reasons) =>
        string.Join(",", reasons
            .GroupBy(static reason => reason)
            .OrderBy(static group => group.Key.ToString(), StringComparer.Ordinal)
            .Select(static group => group.Key + ":" + group.Count().ToString(CultureInfo.InvariantCulture)));
}

internal sealed class LengthPrefixedHash : IDisposable
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public LengthPrefixedHash(string domain)
    {
        Append(domain);
    }

    public void Append(string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, byteCount);
        _hash.AppendData(length);
        if (byteCount == 0)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        _hash.AppendData(bytes);
    }

    public void Append(int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        _hash.AppendData(bytes);
    }

    public void Append(bool value) => Append(value ? 1 : 0);

    public string Finish() => Convert.ToHexString(_hash.GetHashAndReset());

    public void Dispose() => _hash.Dispose();
}
