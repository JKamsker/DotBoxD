using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal sealed class PlatformAvailability
{
    private readonly Dictionary<string, PlatformBoundary[]> platforms;

    private PlatformAvailability(Dictionary<string, PlatformBoundary[]> platforms)
    {
        this.platforms = platforms;
        SupportsUnlistedPlatforms = !platforms.Values.Any(StartsSupported);
    }

    public bool SupportsUnlistedPlatforms { get; }

    public IEnumerable<string> Families => platforms.Keys;

    public static PlatformAvailability FromType(INamedTypeSymbol type)
    {
        var annotations = new Dictionary<string, List<PlatformBoundary>>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in type.GetAttributes())
        {
            if (ReadBoundary(attribute) is not { } annotation)
            {
                continue;
            }

            if (!annotations.TryGetValue(annotation.Family, out var boundaries))
            {
                boundaries = [];
                annotations.Add(annotation.Family, boundaries);
            }

            boundaries.Add(annotation.Boundary);
        }

        return FromBoundaries(annotations);
    }

    public static PlatformAvailability FromBoundaries(Dictionary<string, List<PlatformBoundary>> annotations)
    {
        // The platform analyzer combines implied iOS boundaries with any explicit
        // MacCatalyst annotations before choosing the earliest support/removal versions.
        if (annotations.TryGetValue("ios", out var ios))
        {
            if (annotations.TryGetValue("maccatalyst", out var macCatalyst))
            {
                macCatalyst.AddRange(ios);
            }
            else
            {
                annotations.Add("maccatalyst", ios);
            }
        }

        var normalizedPlatforms = annotations.ToDictionary(
            static pair => pair.Key,
            static pair => Normalize(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        return new PlatformAvailability(normalizedPlatforms);
    }

    public IEnumerable<PlatformBoundary> Boundaries(string family)
        => platforms.TryGetValue(family, out var boundaries) ? boundaries : [];

    public bool Supports(string family, Version version)
    {
        if (!platforms.TryGetValue(family, out var boundaries))
        {
            return SupportsUnlistedPlatforms;
        }

        // For inconsistent allowlist/denylist annotations, the framework analyzer
        // ignores families whose first boundary is unsupported.
        if (!SupportsUnlistedPlatforms && !StartsSupported(boundaries))
        {
            return false;
        }

        var supported = !StartsSupported(boundaries);
        foreach (var boundary in boundaries)
        {
            if (boundary.Version > version)
            {
                break;
            }

            supported = boundary.Supported;
        }

        return supported;
    }

    private static bool StartsSupported(PlatformBoundary[] boundaries)
        => boundaries[0].Supported;

    private static PlatformBoundary[] Normalize(IEnumerable<PlatformBoundary> boundaries)
    {
        // These are the boundaries recognized by the platform compatibility analyzer:
        // the earliest supported version and the two earliest unsupported entries.
        // Duplicate unsupported versions still occupy one of the analyzer's two slots.
        var ordered = boundaries.OrderBy(static boundary => boundary.Version).ToArray();
        return ordered.Where(static boundary => boundary.Supported).Take(1)
            .Concat(ordered.Where(static boundary => !boundary.Supported).Take(2))
            .OrderBy(static boundary => boundary.Version)
            .ThenByDescending(static boundary => boundary.Supported)
            .ToArray();
    }

    private static (string Family, PlatformBoundary Boundary)? ReadBoundary(AttributeData attribute)
    {
        if (!SharedPlatformAttributeSource.IsAvailabilityAttribute(attribute) ||
            attribute.ConstructorArguments.Length == 0 ||
            attribute.ConstructorArguments[0].Value is not string name)
        {
            return null;
        }

        var versionStart = name.TakeWhile(static character => !char.IsDigit(character)).Count();
        var family = name.Substring(0, versionStart).ToLowerInvariant();
        if (family == "osx")
        {
            family = "macos";
        }

        var suffix = name.Substring(versionStart);
        var version = suffix.Length == 0 ? PlatformBoundary.Zero : ParseVersion(suffix);
        var supported = attribute.AttributeClass!.Name == "SupportedOSPlatformAttribute";
        return (family, new PlatformBoundary(version, suffix, supported));
    }

    private static Version ParseVersion(string suffix)
    {
        if (!Version.TryParse(suffix, out var version))
        {
            throw new NotSupportedException("platform availability has an invalid version: " + suffix);
        }

        return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    }
}

internal sealed record PlatformBoundary(Version Version, string Suffix, bool Supported)
{
    public static Version Zero { get; } = new(0, 0, 0, 0);
}
