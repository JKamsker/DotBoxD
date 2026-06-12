namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;

internal sealed record GeneratedPluginPackageResult(GeneratedPluginPackage? Package, Diagnostic? Diagnostic);
