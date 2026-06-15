namespace DotBoxD.Plugins.Analyzer.Analysis;

internal sealed record GeneratedPluginPackage(string HintName, string Source);

internal sealed record GeneratedPluginPackageDiagnostic(string Message);

internal sealed record GeneratedPluginPackageBatch(
    EquatableArray<GeneratedPluginPackage> Packages,
    EquatableArray<GeneratedPluginPackageDiagnostic> Diagnostics);
