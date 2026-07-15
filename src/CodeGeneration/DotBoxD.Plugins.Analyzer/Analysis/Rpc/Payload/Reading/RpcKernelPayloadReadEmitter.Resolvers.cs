namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using Microsoft.CodeAnalysis;

internal sealed partial class RpcKernelPayloadReadEmitter
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
        RpcKernelPayloadReadEmitter emitter,
        ITypeSymbol type,
        string reader,
        out string result);

    private static bool TryReadNullable(
        RpcKernelPayloadReadEmitter emitter,
        ITypeSymbol type,
        string reader,
        out string result)
    {
        if (DotBoxDNullableScalarType.TryGetSupportedUnderlying(type, out var nullableUnderlying))
        {
            result = $"{emitter.EnsureNullablePayloadReader(type, nullableUnderlying)}(ref {reader})";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadGuid(
        RpcKernelPayloadReadEmitter emitter,
        ITypeSymbol type,
        string reader,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.IsGuid(type))
        {
            result = $"{reader}.ReadGuid()";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadDateTime(
        RpcKernelPayloadReadEmitter emitter,
        ITypeSymbol type,
        string reader,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.IsDateTimeWireType(type))
        {
            result = $"{emitter.EnsureDateTimePayloadReader(type)}(ref {reader})";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadDecimal(
        RpcKernelPayloadReadEmitter emitter,
        ITypeSymbol type,
        string reader,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.IsDecimalWireType(type))
        {
            result = $"{emitter.EnsureDecimalPayloadReader()}(ref {reader})";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadDateOnly(
        RpcKernelPayloadReadEmitter emitter,
        ITypeSymbol type,
        string reader,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.IsDateOnlyWireType(type))
        {
            result = $"{emitter.EnsureDateOnlyPayloadReader()}({reader}.ReadInt32())";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadTimeOnly(
        RpcKernelPayloadReadEmitter emitter,
        ITypeSymbol type,
        string reader,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.IsTimeOnlyWireType(type))
        {
            result = $"{emitter.EnsureTimeOnlyPayloadReader()}({reader}.ReadInt64())";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadTimeSpan(
        RpcKernelPayloadReadEmitter emitter,
        ITypeSymbol type,
        string reader,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.IsTimeSpanWireType(type))
        {
            result = $"new global::System.TimeSpan({reader}.ReadInt64())";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadCancellationToken(
        RpcKernelPayloadReadEmitter emitter,
        ITypeSymbol type,
        string reader,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.IsCancellationTokenWireType(type))
        {
            result = $"new global::System.Threading.CancellationToken({reader}.ReadBool())";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadIndex(
        RpcKernelPayloadReadEmitter emitter,
        ITypeSymbol type,
        string reader,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.IsIndexWireType(type))
        {
            result = $"{emitter.EnsureIndexPayloadReader()}(ref {reader})";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadRange(
        RpcKernelPayloadReadEmitter emitter,
        ITypeSymbol type,
        string reader,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.IsRangeWireType(type))
        {
            result = $"{emitter.EnsureRangePayloadReader()}(ref {reader})";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadEnum(
        RpcKernelPayloadReadEmitter emitter,
        ITypeSymbol type,
        string reader,
        out string result)
    {
        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            result = $"{emitter.EnsureEnumReader(enumType)}(ref {reader})";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadList(
        RpcKernelPayloadReadEmitter emitter,
        ITypeSymbol type,
        string reader,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.ListElementType(type) is null)
        {
            result = string.Empty;
            return false;
        }

        result = $"{emitter.EnsureListReader(type)}(ref {reader})";
        return true;
    }

    private static bool TryReadMap(
        RpcKernelPayloadReadEmitter emitter,
        ITypeSymbol type,
        string reader,
        out string result)
    {
        if (DotBoxDRpcTypeMapper.MapTypes(type) is { } map)
        {
            result = $"{emitter.EnsureMapReader(type, map.Key, map.Value)}(ref {reader})";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryReadDto(
        RpcKernelPayloadReadEmitter emitter,
        ITypeSymbol type,
        string reader,
        out string result)
    {
        if (type is INamedTypeSymbol named && DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            result = $"{emitter.EnsureDtoReader(named)}(ref {reader})";
            return true;
        }

        result = string.Empty;
        return false;
    }

}
