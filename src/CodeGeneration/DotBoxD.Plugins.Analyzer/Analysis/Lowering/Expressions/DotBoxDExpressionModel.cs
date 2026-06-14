namespace DotBoxD.Plugins.Analyzer;

internal sealed record DotBoxDExpressionModel(string Source, string Type, bool Allocates);

internal sealed record DotBoxDStatementBodyModel(string Source, bool Allocates);

internal sealed record DotBoxDHandleModel(DotBoxDExpressionModel Target, DotBoxDExpressionModel Message)
{
    public bool Allocates => Target.Allocates || Message.Allocates;
}
