using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class SharedPlatformAttributeSource
{
    public static bool IsAvailabilityAttribute(AttributeData attribute)
        => attribute.AttributeClass?.ToDisplayString() is
            "System.Runtime.Versioning.SupportedOSPlatformAttribute" or
            "System.Runtime.Versioning.UnsupportedOSPlatformAttribute";

    public static IEnumerable<string> FromTypes(IEnumerable<ITypeSymbol?> types)
    {
        var policies = new List<PlatformAvailability>();
        foreach (var type in types)
        {
            Collect(type, policies);
        }

        var supportsUnlisted = policies.All(static policy => policy.SupportsUnlistedPlatforms);
        var attributes = new List<string>();
        var emittedBoundaries = new Dictionary<string, List<PlatformBoundary>>(StringComparer.OrdinalIgnoreCase);
        var hasSupportedPlatform = supportsUnlisted;
        var families = policies.SelectMany(static policy => policy.Families)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static family => family, StringComparer.Ordinal);
        foreach (var family in families)
        {
            var boundaries = Intersect(policies, family, supportsUnlisted);
            PreserveMacCatalystExclusion(policies, family, supportsUnlisted, boundaries);
            hasSupportedPlatform |= boundaries.Any(static boundary => boundary.Supported);
            ValidateRepresentable(boundaries);
            attributes.AddRange(boundaries.Select(boundary => Format(family, boundary)));
            if (boundaries.Count > 0)
            {
                emittedBoundaries.Add(family, boundaries);
            }
        }

        if (!hasSupportedPlatform)
        {
            throw new NotSupportedException("the receiver, input, and output types do not share a supported platform.");
        }

        ValidateImpliedMacCatalystSupport(policies, emittedBoundaries);
        return attributes;
    }

    private static void ValidateImpliedMacCatalystSupport(
        IReadOnlyList<PlatformAvailability> policies,
        Dictionary<string, List<PlatformBoundary>> emittedBoundaries)
    {
        if (!emittedBoundaries.ContainsKey("ios"))
        {
            return;
        }

        var emitted = PlatformAvailability.FromBoundaries(emittedBoundaries);
        var versions = policies.SelectMany(static policy => policy.Boundaries("maccatalyst"))
            .Concat(emitted.Boundaries("maccatalyst"))
            .Select(static boundary => boundary.Version).Append(PlatformBoundary.Zero).Distinct();
        if (versions.Any(version => emitted.Supports("maccatalyst", version) !=
            policies.All(policy => policy.Supports("maccatalyst", version))))
        {
            throw new NotSupportedException(
                "the shared platform availability cannot represent MacCatalyst support alongside implied iOS support.");
        }
    }

    private static void PreserveMacCatalystExclusion(
        IReadOnlyList<PlatformAvailability> policies,
        string family,
        bool supportsUnlisted,
        ICollection<PlatformBoundary> boundaries)
    {
        if (family == "maccatalyst" && !supportsUnlisted && boundaries.Count == 0 &&
            Intersect(policies, "ios", supportsUnlisted).Any(static boundary => boundary.Supported))
        {
            // An absent MacCatalyst annotation would inherit the emitted iOS support.
            // Preserve an explicit override when this intersection excludes MacCatalyst.
            boundaries.Add(new PlatformBoundary(PlatformBoundary.Zero, string.Empty, Supported: false));
        }
    }

    private static List<PlatformBoundary> Intersect(
        IReadOnlyList<PlatformAvailability> policies,
        string family,
        bool supportsUnlisted)
    {
        var boundaries = policies.SelectMany(policy => policy.Boundaries(family))
            .Append(new PlatformBoundary(PlatformBoundary.Zero, string.Empty, supportsUnlisted))
            .GroupBy(static boundary => boundary.Version)
            .Select(static group => group.First())
            .OrderBy(static boundary => boundary.Version);
        var result = new List<PlatformBoundary>();
        var previous = supportsUnlisted;
        foreach (var boundary in boundaries)
        {
            var supported = policies.All(policy => policy.Supports(family, boundary.Version));
            if (supported != previous)
            {
                result.Add(boundary with { Supported = supported });
                previous = supported;
            }
        }

        return result;
    }

    private static void ValidateRepresentable(IReadOnlyCollection<PlatformBoundary> boundaries)
    {
        if (boundaries.Count(static boundary => boundary.Supported) > 1 ||
            boundaries.Count(static boundary => !boundary.Supported) > 2)
        {
            throw new NotSupportedException(
                "the shared platform availability has multiple support intervals that platform attributes cannot represent.");
        }
    }

    private static string Format(string family, PlatformBoundary boundary)
        => "[global::System.Runtime.Versioning." +
            (boundary.Supported ? "SupportedOSPlatformAttribute" : "UnsupportedOSPlatformAttribute") +
            "(" + LiteralReader.StringLiteral(family + boundary.Suffix) + ")]\n";

    private static void Collect(ITypeSymbol? type, ICollection<PlatformAvailability> policies)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                Collect(array.ElementType, policies);
                break;
            case INamedTypeSymbol named:
                policies.Add(PlatformAvailability.FromType(named));
                foreach (var argument in named.TypeArguments)
                {
                    Collect(argument, policies);
                }

                break;
        }
    }
}
