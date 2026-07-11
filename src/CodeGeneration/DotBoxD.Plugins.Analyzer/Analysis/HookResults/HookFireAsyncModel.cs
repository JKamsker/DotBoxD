namespace DotBoxD.Plugins.Analyzer.Analysis.HookResults;

internal sealed record HookFireAsyncModel(
    string ContextTypeFullName,
    string ResultTypeFullName,
    EquatableArray<string> Attributes,
    string Accessibility,
    bool IsAssemblyClsCompliant);

internal sealed record HookFireAsyncModelResult(
    HookFireAsyncModel? Model,
    PluginKernelDiagnostic? Diagnostic);
