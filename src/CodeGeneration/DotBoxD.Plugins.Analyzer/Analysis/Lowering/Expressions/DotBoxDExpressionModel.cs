namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal sealed record DotBoxDExpressionModel(string Source, string Type, bool Allocates);

internal sealed record DotBoxDStatementBodyModel(string Source, bool Allocates);

internal sealed record DotBoxDHandleModel(DotBoxDExpressionModel Target, DotBoxDExpressionModel Message)
{
    public bool Allocates => Target.Allocates || Message.Allocates;
}
