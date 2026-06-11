namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis.CSharp.Syntax;

internal sealed record PluginKernelModel(
    string PluginId,
    string Namespace,
    string KernelName,
    string PackageName,
    string EventName,
    string EventParameterName,
    string ContextParameterName,
    IReadOnlyList<EventPropertyModel> EventProperties,
    IReadOnlyList<LiveSettingModel> LiveSettings,
    MethodDeclarationSyntax ShouldHandle,
    MethodDeclarationSyntax Handle);

internal sealed record EventPropertyModel(string Name, string Type);

internal sealed record LiveSettingModel(
    string Name,
    string Type,
    string DefaultValue,
    string? Min,
    string? Max);
