using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class ExperimentalAttributeSource
{
    private const string ExperimentalAttributeName = "System.Diagnostics.CodeAnalysis.ExperimentalAttribute";
    private const string SupportedOSPlatformAttributeName =
        "System.Runtime.Versioning.SupportedOSPlatformAttribute";
    private const string RequiresDynamicCodeAttributeName =
        "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute";
    private const string RequiresUnreferencedCodeAttributeName =
        "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute";
    private static readonly HashSet<string> PlatformCompatibilityAttributeNames =
    [
        "System.Runtime.Versioning.SupportedOSPlatformAttribute",
        "System.Runtime.Versioning.UnsupportedOSPlatformAttribute",
        "System.Runtime.Versioning.ObsoletedOSPlatformAttribute",
    ];

    public static string FromTypes(params ITypeSymbol?[] types)
    {
        var diagnosticIds = new SortedSet<string>(StringComparer.Ordinal);
        var codeRequirementAttributes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var platformAttributes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            Collect(type, diagnosticIds, codeRequirementAttributes, platformAttributes, includeSupportedPlatforms: true);
        }

        return BuildSource(diagnosticIds, codeRequirementAttributes, platformAttributes);
    }

    public static string FromTypesWithSharedSupportedPlatform(params ITypeSymbol?[] types)
    {
        var diagnosticIds = new SortedSet<string>(StringComparer.Ordinal);
        var codeRequirementAttributes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var platformAttributes = new SortedSet<string>(StringComparer.Ordinal);
        var restrictions = new List<Dictionary<string, PlatformSupport>>();
        foreach (var type in types)
        {
            Collect(type, diagnosticIds, codeRequirementAttributes, platformAttributes, includeSupportedPlatforms: false);
            CollectSupportedPlatformRestrictions(type, restrictions);
        }

        platformAttributes.UnionWith(SharedSupportedPlatformAttributes(restrictions));
        return BuildSource(diagnosticIds, codeRequirementAttributes, platformAttributes);
    }

    private static string BuildSource(
        SortedSet<string> diagnosticIds,
        SortedDictionary<string, string> codeRequirementAttributes,
        SortedSet<string> platformAttributes)
    {
        var source = string.Empty;
        if (diagnosticIds.Count > 0)
        {
            source = "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(" +
                LiteralReader.StringLiteral(diagnosticIds.Min!) +
                ")]\n";
        }

        return source + string.Concat(codeRequirementAttributes.Values) + string.Concat(platformAttributes);
    }

    private static void Collect(
        ITypeSymbol? type,
        ISet<string> diagnosticIds,
        IDictionary<string, string> codeRequirementAttributes,
        ISet<string> platformAttributes,
        bool includeSupportedPlatforms)
    {
        switch (type)
        {
            case null:
                return;
            case IArrayTypeSymbol array:
                Collect(array.ElementType, diagnosticIds, codeRequirementAttributes, platformAttributes, includeSupportedPlatforms);
                return;
            case INamedTypeSymbol named:
                CollectNamed(named, diagnosticIds, codeRequirementAttributes, platformAttributes, includeSupportedPlatforms);
                return;
        }
    }

    private static void CollectNamed(
        INamedTypeSymbol named,
        ISet<string> diagnosticIds,
        IDictionary<string, string> codeRequirementAttributes,
        ISet<string> platformAttributes,
        bool includeSupportedPlatforms)
    {
        foreach (var attribute in named.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == ExperimentalAttributeName &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is string diagnosticId)
            {
                diagnosticIds.Add(diagnosticId);
            }

            CollectCodeRequirementAttribute(attribute, codeRequirementAttributes);
            if (PlatformCompatibilityAttributeSource(attribute) is { } source &&
                (includeSupportedPlatforms || !IsSupportedPlatformAttribute(attribute)))
            {
                platformAttributes.Add(source);
            }
        }

        foreach (var argument in named.TypeArguments)
        {
            Collect(argument, diagnosticIds, codeRequirementAttributes, platformAttributes, includeSupportedPlatforms);
        }
    }

    private static void CollectSupportedPlatformRestrictions(
        ITypeSymbol? type,
        ICollection<Dictionary<string, PlatformSupport>> restrictions)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                CollectSupportedPlatformRestrictions(array.ElementType, restrictions);
                return;
            case INamedTypeSymbol named:
                var supportedPlatforms = SupportedPlatforms(named);
                if (supportedPlatforms.Count > 0)
                {
                    restrictions.Add(supportedPlatforms);
                }

                foreach (var argument in named.TypeArguments)
                {
                    CollectSupportedPlatformRestrictions(argument, restrictions);
                }

                return;
        }
    }

    private static Dictionary<string, PlatformSupport> SupportedPlatforms(INamedTypeSymbol type)
    {
        var platforms = new Dictionary<string, PlatformSupport>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in type.GetAttributes())
        {
            if (!IsSupportedPlatformAttribute(attribute) ||
                attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not string name)
            {
                continue;
            }

            var platform = new PlatformSupport(name);
            if (!platforms.TryGetValue(platform.Family, out var existing) || platform.IsMoreRestrictiveThan(existing))
            {
                platforms[platform.Family] = platform;
            }
        }

        return platforms;
    }

    private static IEnumerable<string> SharedSupportedPlatformAttributes(
        IReadOnlyList<Dictionary<string, PlatformSupport>> restrictions)
    {
        if (restrictions.Count == 0)
        {
            return Enumerable.Empty<string>();
        }

        var shared = new Dictionary<string, PlatformSupport>(restrictions[0], StringComparer.OrdinalIgnoreCase);
        foreach (var restriction in restrictions.Skip(1))
        {
            foreach (var family in shared.Keys.Except(restriction.Keys, StringComparer.OrdinalIgnoreCase).ToArray())
            {
                shared.Remove(family);
            }

            foreach (var pair in restriction)
            {
                var family = pair.Key;
                var platform = pair.Value;
                if (shared.TryGetValue(family, out var existing) && platform.IsMoreRestrictiveThan(existing))
                {
                    shared[family] = platform;
                }
            }
        }

        if (shared.Count == 0)
        {
            throw new NotSupportedException("the receiver, input, and output types do not share a supported platform.");
        }

        return shared.Values
            .OrderBy(static platform => platform.Name, StringComparer.Ordinal)
            .Select(static platform =>
                "[global::System.Runtime.Versioning.SupportedOSPlatformAttribute(" +
                LiteralReader.StringLiteral(platform.Name) + ")]\n");
    }

    private static bool IsSupportedPlatformAttribute(AttributeData attribute)
        => attribute.AttributeClass?.ToDisplayString() == SupportedOSPlatformAttributeName;

    private static void CollectCodeRequirementAttribute(
        AttributeData attribute,
        IDictionary<string, string> codeRequirementAttributes)
    {
        var attributeClass = attribute.AttributeClass;
        var name = attributeClass?.ToDisplayString();
        if (name is not RequiresDynamicCodeAttributeName and not RequiresUnreferencedCodeAttributeName ||
            attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value is not string message)
        {
            return;
        }

        var url = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == "Url").Value.Value as string;
        var urlAssignment = url is null
            ? string.Empty
            : ", Url = " + LiteralReader.StringLiteral(url);
        if (!codeRequirementAttributes.ContainsKey(attributeClass!.Name))
        {
            codeRequirementAttributes.Add(
                attributeClass.Name,
                "[global::System.Diagnostics.CodeAnalysis." + attributeClass.Name + "(" +
                LiteralReader.StringLiteral(message) + urlAssignment + ")]\n");
        }
    }

    private static string? PlatformCompatibilityAttributeSource(AttributeData attribute)
    {
        var attributeType = attribute.AttributeClass;
        if (attributeType is null ||
            !PlatformCompatibilityAttributeNames.Contains(attributeType.ToDisplayString()) ||
            attribute.ConstructorArguments.Length == 0 ||
            attribute.ConstructorArguments.Any(argument => argument.Value is not string))
        {
            return null;
        }

        var arguments = string.Join(
            ", ",
            attribute.ConstructorArguments.Select(argument => LiteralReader.StringLiteral((string)argument.Value!)));
        return "[" + attributeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            "(" + arguments + ")]\n";
    }

    private sealed class PlatformSupport
    {
        public PlatformSupport(string name)
        {
            Name = name;
            var versionStart = name.TakeWhile(static character => !char.IsDigit(character)).Count();
            Family = name.Substring(0, versionStart);
            Version = versionStart == name.Length || !System.Version.TryParse(name.Substring(versionStart), out var version)
                ? null
                : version;
        }

        public string Family { get; }

        public string Name { get; }

        private Version? Version { get; }

        public bool IsMoreRestrictiveThan(PlatformSupport other)
            => Version is not null && (other.Version is null || Version > other.Version);
    }
}
