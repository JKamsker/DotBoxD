using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class PluginAnalyzerDiagnostics
{
    internal const string ShippedRulesHelpLinkBase =
        "https://github.com/JKamsker/Safe-IR/blob/main/src/DotBoxD.Plugins.Analyzer/AnalyzerReleases.Shipped.md#";

    internal const string UnshippedRulesHelpLinkBase =
        "https://github.com/JKamsker/Safe-IR/blob/main/src/DotBoxD.Plugins.Analyzer/AnalyzerReleases.Unshipped.md#";

    public static readonly DiagnosticDescriptor UnsupportedKernelShapeRule = new(
        "DBXK100",
        "Plugin kernel shape is not supported",
        "{0}",
        "DotBoxD.Kernels.Generation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Plugin package generation supports a restricted kernel expression subset; interpolation holes may be strings or supported invariant string-convertible numeric types.",
        helpLinkUri: UnshippedRulesHelpLinkBase + "DBXK100");

    // A remote RunLocal chain (RemoteHookRegistry / RemoteSubscriptionRegistry) is only intercepted when its
    // Where/Select stages lower to verified IR. When a stage cannot be lowered (an unsupported projection or
    // predicate) the generator fails safe and emits no interceptor, so the native terminal throws
    // NotSupportedException at runtime. This surfaces that cause at compile time instead of leaving a silent skip.
    public static readonly DiagnosticDescriptor RunLocalNotLoweredRule = new(
        "DBXK111",
        "RunLocal chain is not lowered and will throw at runtime",
        "This remote RunLocal chain could not be lowered to verified IR (an unsupported Where/Select projection or "
            + "predicate), so the generator does not intercept it and the runtime terminal throws "
            + "NotSupportedException; use a supported projection/predicate shape",
        "DotBoxD.Kernels.Generation",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A recognized remote RunLocal hook chain whose Where/Select stages cannot be lowered is skipped "
            + "by the generator; without interception its native terminal throws at runtime.",
        helpLinkUri: UnshippedRulesHelpLinkBase + "DBXK111");
}
