using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class DotBoxDNullableScalarType
{
    public static bool TryGetSupportedUnderlying(ITypeSymbol type, out ITypeSymbol underlying)
    {
        if (type is INamedTypeSymbol named &&
            named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T &&
            IsSupportedUnderlying(named.TypeArguments[0]))
        {
            underlying = named.TypeArguments[0];
            return true;
        }

        underlying = null!;
        return false;
    }

    public static bool IsNullableValueType(ITypeSymbol type)
        => type is INamedTypeSymbol named &&
           named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T;

    public static bool IsSupportedUnderlying(ITypeSymbol type)
    {
        if (IsSupportedSpecialType(type.SpecialType))
        {
            return true;
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        return IsSupportedWireStruct(type);
    }

    private static bool IsSupportedSpecialType(SpecialType specialType)
        => specialType is SpecialType.System_Boolean
            or SpecialType.System_Int32
            or SpecialType.System_Int64
            or SpecialType.System_Double
            or SpecialType.System_Single
            or SpecialType.System_Decimal;

    private static bool IsSupportedWireStruct(ITypeSymbol type)
    {
        if (DotBoxDRpcTypeMapper.IsGuid(type))
        {
            return true;
        }

        if (DotBoxDRpcTypeMapper.IsDateTimeWireType(type))
        {
            return true;
        }

        if (DotBoxDRpcTypeMapper.IsDateOnlyWireType(type))
        {
            return true;
        }

        if (DotBoxDRpcTypeMapper.IsTimeOnlyWireType(type))
        {
            return true;
        }

        if (DotBoxDRpcTypeMapper.IsTimeSpanWireType(type))
        {
            return true;
        }

        return DotBoxDRpcTypeMapper.IsCancellationTokenWireType(type);
    }
}
