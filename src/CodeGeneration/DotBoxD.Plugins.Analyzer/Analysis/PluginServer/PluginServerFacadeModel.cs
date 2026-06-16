using DotBoxD.Plugins.Analyzer.Analysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal sealed record PluginServerFacadeModel(
    string Namespace,
    string Accessibility,
    string ClassName,
    string WorldType,
    string ControlServiceType,
    string LiveSettingUpdateType,
    EquatableArray<PluginServerForwardedMethod> WorldMethods,
    EquatableArray<PluginServerControlProperty> Controls);

internal sealed record PluginServerControlProperty(
    string Name,
    string Type,
    string WrapperName,
    string ClientInterfaceName,
    EquatableArray<PluginServerForwardedMethod> Methods,
    EquatableArray<PluginServerServiceWrapper> ServiceWrappers);

internal sealed record PluginServerForwardedMethod(
    string Name,
    string ReturnType,
    string? ReturnWrapperName,
    EquatableArray<PluginServerParameter> Parameters);

internal sealed record PluginServerForwardedProperty(string Name, string Type);

internal sealed record PluginServerServiceWrapper(
    string Type,
    string WrapperName,
    EquatableArray<PluginServerForwardedProperty> Properties,
    EquatableArray<PluginServerForwardedMethod> Methods);

internal sealed record PluginServerParameter(string Name, string Type);

internal sealed record PluginServerFacadeResult(
    GeneratedPluginPackage? Source,
    PluginKernelDiagnostic? Diagnostic);
