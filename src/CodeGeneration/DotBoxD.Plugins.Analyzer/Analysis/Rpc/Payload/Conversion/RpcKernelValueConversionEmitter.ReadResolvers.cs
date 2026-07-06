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
        => TryWriteWhen(DotBoxDRpcTypeMapper.IsGuid(type), $"{expression}.GuidValue", out result);

    private static bool TryReadDateTime(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsDateTimeWireType(type),
            $"{emitter.EnsureDateTimeValueReader(type)}({expression})",
            out result);

    private static bool TryReadDecimal(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsDecimalWireType(type),
            $"{emitter.EnsureDecimalValueReader()}({expression})",
            out result);

    private static bool TryReadDateOnly(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsDateOnlyWireType(type),
            $"{emitter.EnsureDateOnlyValueReader()}({expression}.Int32Value)",
            out result);

    private static bool TryReadTimeOnly(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsTimeOnlyWireType(type),
            $"{emitter.EnsureTimeOnlyValueReader()}({expression}.Int64Value)",
            out result);

    private static bool TryReadTimeSpan(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsTimeSpanWireType(type),
            $"new global::System.TimeSpan({expression}.Int64Value)",
            out result);

    private static bool TryReadCancellationToken(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsCancellationTokenWireType(type),
            $"new global::System.Threading.CancellationToken({expression}.BoolValue)",
            out result);

    private static bool TryReadIndex(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsIndexWireType(type),
            $"{emitter.EnsureIndexValueReader()}({expression})",
            out result);

    private static bool TryReadRange(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsRangeWireType(type),
            $"{emitter.EnsureRangeValueReader()}({expression})",
            out result);

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
