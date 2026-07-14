using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcNumericConversionClassifier
{
    private static readonly Dictionary<SpecialType, (int Minimum, int Maximum)> NarrowI32Ranges = new()
    {
        [SpecialType.System_SByte] = (sbyte.MinValue, sbyte.MaxValue),
        [SpecialType.System_Byte] = (byte.MinValue, byte.MaxValue),
        [SpecialType.System_Int16] = (short.MinValue, short.MaxValue),
        [SpecialType.System_UInt16] = (ushort.MinValue, ushort.MaxValue)
    };

    private static readonly HashSet<(SpecialType Source, SpecialType Target)> ImplicitI32WireConversions =
    [
        (SpecialType.System_SByte, SpecialType.System_Int16),
        (SpecialType.System_SByte, SpecialType.System_Int32),
        (SpecialType.System_Byte, SpecialType.System_Int16),
        (SpecialType.System_Byte, SpecialType.System_UInt16),
        (SpecialType.System_Byte, SpecialType.System_Int32),
        (SpecialType.System_Int16, SpecialType.System_Int32),
        (SpecialType.System_UInt16, SpecialType.System_Int32),
        (SpecialType.System_Char, SpecialType.System_UInt16),
        (SpecialType.System_Char, SpecialType.System_Int32)
    ];

    public static bool IsRepresentableNarrowI32Constant(
        SemanticModel model,
        ExpressionSyntax expression,
        ITypeSymbol sourceType,
        ITypeSymbol convertedType,
        CancellationToken cancellationToken)
    {
        if (sourceType.SpecialType != SpecialType.System_Int32 ||
            !NarrowI32Ranges.TryGetValue(convertedType.SpecialType, out var range) ||
            model.GetConstantValue(expression, cancellationToken).Value is not int value)
        {
            return false;
        }

        return value >= range.Minimum && value <= range.Maximum;
    }

    public static bool IsImplicitI32WireConversion(ITypeSymbol sourceType, ITypeSymbol targetType)
        => ImplicitI32WireConversions.Contains((sourceType.SpecialType, targetType.SpecialType));
}
