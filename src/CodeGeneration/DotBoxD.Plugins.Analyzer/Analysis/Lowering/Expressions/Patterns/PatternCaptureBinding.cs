using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal sealed record PatternCaptureBinding(
    DotBoxDExpressionModel Key,
    INamedTypeSymbol Subtype);
