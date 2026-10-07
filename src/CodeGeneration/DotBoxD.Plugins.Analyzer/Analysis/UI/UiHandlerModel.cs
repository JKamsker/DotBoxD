using Microsoft.CodeAnalysis.Text;

namespace DotBoxD.Plugins.Analyzer.Analysis.UI;

internal sealed record UiHandlerModel(string MethodName, string HintName, string? Source, string? Error,
    string Path, TextSpan Span, LinePositionSpan LineSpan);
