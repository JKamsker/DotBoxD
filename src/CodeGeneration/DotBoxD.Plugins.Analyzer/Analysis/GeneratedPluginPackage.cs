using DotBoxD.Plugins.Analyzer.Analysis.Lowering;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal sealed record GeneratedPluginPackage(
    string HintName,
    string Source,
    string Namespace,
    string PackageName);

internal readonly record struct GeneratedPluginPackageIdentity(
    string Namespace,
    string PackageName)
{
    public static GeneratedPluginPackageIdentity From(GeneratedPluginPackage package)
    {
        if (!package.PackageName.EndsWith(DotBoxDGenerationNames.PluginPackageSuffix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Collision-tracked package name '{package.PackageName}' must end with " +
                $"'{DotBoxDGenerationNames.PluginPackageSuffix}'.");
        }

        return new GeneratedPluginPackageIdentity(package.Namespace, package.PackageName);
    }

    public string NamespaceDisplay
        => string.IsNullOrWhiteSpace(Namespace) ? "<global>" : Namespace;
}

internal sealed record GeneratedPluginPackageDiagnostic(string Message);
