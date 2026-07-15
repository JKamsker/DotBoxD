using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcNumericConversion
{
    private static readonly HashSet<(SpecialType Source, SpecialType Target)> SameWireConversions =
    [
        (SpecialType.System_SByte, SpecialType.System_Int16),
        (SpecialType.System_SByte, SpecialType.System_Int32),
        (SpecialType.System_Byte, SpecialType.System_Int16),
        (SpecialType.System_Byte, SpecialType.System_UInt16),
        (SpecialType.System_Byte, SpecialType.System_Int32),
        (SpecialType.System_Int16, SpecialType.System_Int32),
        (SpecialType.System_UInt16, SpecialType.System_Int32),
        (SpecialType.System_Char, SpecialType.System_UInt16),
        (SpecialType.System_Char, SpecialType.System_Int32),
        (SpecialType.System_Single, SpecialType.System_Double)
    ];

    public static bool IsSupported(ITypeSymbol sourceType, ITypeSymbol targetType)
        => Classify(sourceType, targetType) is not NumericConversionKind.Unsupported;

    public static bool IsRepresentableNarrowI32Constant(int value, ITypeSymbol targetType)
        => targetType.SpecialType switch
        {
            SpecialType.System_SByte => IsInRange(value, sbyte.MinValue, sbyte.MaxValue),
            SpecialType.System_Byte => IsInRange(value, byte.MinValue, byte.MaxValue),
            SpecialType.System_Int16 => IsInRange(value, short.MinValue, short.MaxValue),
            SpecialType.System_UInt16 => IsInRange(value, ushort.MinValue, ushort.MaxValue),
            _ => false
        };

    public static bool TryApply(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        string lowered,
        out string converted)
    {
        switch (Classify(sourceType, targetType))
        {
            case NumericConversionKind.Identity:
            case NumericConversionKind.SameWire:
                converted = lowered;
                return true;
            case NumericConversionKind.ToI64:
                converted = DotBoxDRpcJsonLowerer.Call("numeric.toI64", null, lowered);
                return true;
            case NumericConversionKind.ToF64:
                converted = DotBoxDRpcJsonLowerer.Call("numeric.toF64", null, lowered);
                return true;
            default:
                converted = lowered;
                return false;
        }
    }

    private static NumericConversionKind Classify(ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
        {
            return NumericConversionKind.Identity;
        }

        if (SameWireConversions.Contains((sourceType.SpecialType, targetType.SpecialType)))
        {
            return NumericConversionKind.SameWire;
        }

        if (IsI32WireIntegral(sourceType) && targetType.SpecialType == SpecialType.System_Int64)
        {
            return NumericConversionKind.ToI64;
        }

        return CanWidenToDouble(sourceType) && targetType.SpecialType == SpecialType.System_Double
            ? NumericConversionKind.ToF64
            : NumericConversionKind.Unsupported;
    }

    private static bool IsInRange(int value, int minimum, int maximum)
        => value >= minimum && value <= maximum;

    private static bool IsI32WireIntegral(ITypeSymbol type)
        => type.SpecialType is SpecialType.System_Byte or
            SpecialType.System_Char or
            SpecialType.System_Int16 or
            SpecialType.System_Int32 or
            SpecialType.System_SByte or
            SpecialType.System_UInt16;

    private static bool CanWidenToDouble(ITypeSymbol type)
        => IsI32WireIntegral(type) || type.SpecialType == SpecialType.System_Int64;

    private enum NumericConversionKind
    {
        Unsupported,
        Identity,
        SameWire,
        ToI64,
        ToF64
    }
}
