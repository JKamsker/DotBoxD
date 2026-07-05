namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using Microsoft.CodeAnalysis;

internal sealed partial class RpcKernelValueConversionEmitter
{
    private static readonly ComplexWriteResolver[] ComplexWriteResolvers =
    [
        TryWriteNullable,
        TryWriteGuid,
        TryWriteDateTime,
        TryWriteDecimal,
        TryWriteDateOnly,
        TryWriteTimeOnly,
        TryWriteTimeSpan,
        TryWriteCancellationToken,
        TryWriteIndex,
        TryWriteRange,
        TryWriteEnum,
        TryWriteList,
        TryWriteMap,
        TryWriteDto,
    ];

    private delegate bool ComplexWriteResolver(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result);

    private static bool TryWriteNullable(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
    {
        if (DotBoxDNullableScalarType.TryGetSupportedUnderlying(type, out var nullableUnderlying))
        {
            result = $"{emitter.EnsureNullableValueWriter(type, nullableUnderlying)}({expression})";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryWriteGuid(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsGuid(type),
            $"{DotBoxDRpcValueNames.GlobalKernelRpcValue}.Guid({expression})",
            out result);

    private static bool TryWriteDateTime(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsDateTimeWireType(type),
            $"{emitter.EnsureDateTimeValueWriter(type)}({expression})",
            out result);

    private static bool TryWriteDecimal(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsDecimalWireType(type),
            $"{emitter.EnsureDecimalValueWriter()}({expression})",
            out result);

    private static bool TryWriteDateOnly(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsDateOnlyWireType(type),
            $"{DotBoxDRpcValueNames.GlobalKernelRpcValue}.Int32({expression}.DayNumber)",
            out result);

    private static bool TryWriteTimeOnly(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsTimeOnlyWireType(type),
            $"{DotBoxDRpcValueNames.GlobalKernelRpcValue}.Int64({expression}.Ticks)",
            out result);

    private static bool TryWriteTimeSpan(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsTimeSpanWireType(type),
            $"{DotBoxDRpcValueNames.GlobalKernelRpcValue}.Int64({expression}.Ticks)",
            out result);

    private static bool TryWriteCancellationToken(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsCancellationTokenWireType(type),
            $"{DotBoxDRpcValueNames.GlobalKernelRpcValue}.Bool({expression}.IsCancellationRequested)",
            out result);

    private static bool TryWriteIndex(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsIndexWireType(type),
            $"{emitter.EnsureIndexValueWriter()}({expression})",
            out result);

    private static bool TryWriteRange(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsRangeWireType(type),
            $"{emitter.EnsureRangeValueWriter()}({expression})",
            out result);

    private static bool TryWriteEnum(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
    {
        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            result = DotBoxDRpcTypeMapper.EnumUsesI64(enumType)
                ? $"{DotBoxDRpcValueNames.GlobalKernelRpcValue}.Int64(unchecked((long){expression}))"
                : $"{DotBoxDRpcValueNames.GlobalKernelRpcValue}.Int32(unchecked((int){expression}))";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryWriteList(
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

        result = $"{emitter.EnsureListWriter(type)}({expression})";
        return true;
    }

    private static bool TryWriteMap(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.MapTypes(type) is { } map)
        {
            result = $"{emitter.EnsureMapWriter(type, map.Key, map.Value)}({expression})";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryWriteDto(
        RpcKernelValueConversionEmitter emitter,
        ITypeSymbol type,
        string expression,
        out string result)
    {
        if (type is INamedTypeSymbol named && DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            result = $"{emitter.EnsureDtoWriter(named)}({expression})";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryWriteWhen(bool condition, string value, out string result)
    {
        result = condition ? value : string.Empty;
        return condition;
    }
}
