namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using Microsoft.CodeAnalysis;

internal sealed partial class RpcKernelValueConversionEmitter
{
    private static readonly ComplexReadResolver[] ComplexReadResolvers =
    [
        TryReadNullable,
        TryReadGuid,
        TryReadDateTime,
        TryReadDecimal,
        TryReadDateOnly,
        TryReadTimeOnly,
        TryReadTimeSpan,
        TryReadCancellationToken,
        TryReadIndex,
        TryReadRange,
        TryReadEnum,
        TryReadList,
        TryReadMap,
        TryReadDto,
    ];

    private delegate bool ComplexReadResolver(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result);

    private static bool TryReadNullable(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
    {
        if (DotBoxDNullableScalarType.TryGetSupportedUnderlying(type, out var nullableUnderlying))
        {
            result = $"{emitter.EnsureNullableValueReader(type, nullableUnderlying)}({expression})";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadGuid(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.IsGuid(type))
        {
            result = $"{expression}.GuidValue";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadDateTime(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.IsDateTimeWireType(type))
        {
            result = $"{emitter.EnsureDateTimeValueReader(type)}({expression})";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadDecimal(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.IsDecimalWireType(type))
        {
            result = $"{emitter.EnsureDecimalValueReader()}({expression})";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadDateOnly(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.IsDateOnlyWireType(type))
        {
            result = $"{emitter.EnsureDateOnlyValueReader()}({expression}.Int32Value)";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadTimeOnly(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.IsTimeOnlyWireType(type))
        {
            result = $"{emitter.EnsureTimeOnlyValueReader()}({expression}.Int64Value)";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadTimeSpan(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.IsTimeSpanWireType(type))
        {
            result = $"new global::System.TimeSpan({expression}.Int64Value)";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadCancellationToken(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.IsCancellationTokenWireType(type))
        {
            result = $"new global::System.Threading.CancellationToken({expression}.BoolValue)";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadIndex(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.IsIndexWireType(type))
        {
            result = $"{emitter.EnsureIndexValueReader()}({expression})";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadRange(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.IsRangeWireType(type))
        {
            result = $"{emitter.EnsureRangeValueReader()}({expression})";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadEnum(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
    {
        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            result = $"{emitter.EnsureEnumValueReader(enumType)}({expression})";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadList(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.ListElementType(type) is null)
        {
            result = string.Empty;
            return false;
        }

        result = $"{emitter.EnsureListReader(type)}({expression})";
        return true;
    }

    private static bool TryReadMap(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.MapTypes(type) is { } map)
        {
            result = $"{emitter.EnsureMapReader(type, map.Key, map.Value)}({expression})";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadDto(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
    {
        if (type is INamedTypeSymbol named && DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            result = $"{emitter.EnsureDtoReader(named)}({expression})";
            return true;
        }

        result = string.Empty;
        return false;
    }
}
