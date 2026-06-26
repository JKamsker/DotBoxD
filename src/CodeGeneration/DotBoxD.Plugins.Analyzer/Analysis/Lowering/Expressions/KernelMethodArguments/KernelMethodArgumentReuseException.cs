namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal sealed class KernelMethodArgumentReuseException(
    string message,
    PluginDiagnosticLocation? location)
    : NotSupportedException(message)
{
    public PluginDiagnosticLocation? Location { get; } = location;
}
