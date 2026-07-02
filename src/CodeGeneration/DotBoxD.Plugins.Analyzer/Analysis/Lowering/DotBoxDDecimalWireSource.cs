using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDDecimalWireSource
{
    public static string RecordSource(ITypeSymbol type, decimal value)
    {
        var bits = decimal.GetBits(value);
        return DotBoxDRecordCreationExpressionLowerer.RecordNew(
            [
                Int32Source(bits[0]),
                Int32Source(bits[1]),
                Int32Source(bits[2]),
                Int32Source(bits[3])
            ],
            SandboxTypeSourceEmitter.TryEmit(type) ?? throw new NotSupportedException());
    }

    private static string Int32Source(int value)
        => $"{DotBoxDGenerationNames.Helpers.I32}({LiteralReader.ObjectLiteral(value)})";
}
