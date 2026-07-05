namespace DotBoxD.Plugins.Analyzer.Analysis.HookResults;

internal sealed record HookFireAsyncModel(
    string ContextTypeFullName,
    string ResultTypeFullName,
    string Accessibility);

internal sealed record HookFireAsyncModelResult(
    HookFireAsyncModel? Model,
    PluginKernelDiagnostic? Diagnostic);
