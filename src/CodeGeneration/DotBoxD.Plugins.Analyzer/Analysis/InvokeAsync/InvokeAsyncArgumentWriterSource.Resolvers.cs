using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static partial class InvokeAsyncArgumentWriterSource
{
    private static readonly ComplexWriteResolver[] ComplexWriteResolvers =
    [
        TryWriteGuid,
        TryWriteDateTime,
        TryWriteDecimal,
        TryWriteTimeSpan,
        TryWriteDateOnly,
        TryWriteTimeOnly,
        TryWriteIndex,
        TryWriteRange,
        TryWriteEnum,
        TryWriteList,
        TryWriteMap,
        TryWriteRecord,
    ];

    private delegate bool ComplexWriteResolver(
        ITypeSymbol type,
        string expression,
        int depth,
        out string result);

    private static bool TryWriteGuid(ITypeSymbol type, string expression, int depth, out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsGuid(type),
            $"{DotBoxDRpcValueNames.GlobalKernelRpcValue}.Guid({expression})",
            out result);

    private static bool TryWriteDateTime(ITypeSymbol type, string expression, int depth, out string result)
    {
        if (DotBoxDRpcTypeMapper.IsDateTimeWireType(type))
        {
            result = IsDateTimeOffset(type)
                ? WriteDateTimeOffsetExpression(expression, depth)
                : WriteDateTimeExpression(expression, depth);
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryWriteDecimal(ITypeSymbol type, string expression, int depth, out string result)
        => TryWriteWhen(DotBoxDRpcTypeMapper.IsDecimalWireType(type), WriteDecimalExpression(expression, depth), out result);

    private static bool TryWriteTimeSpan(ITypeSymbol type, string expression, int depth, out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsTimeSpanWireType(type),
            $"{DotBoxDRpcValueNames.GlobalKernelRpcValue}.Int64({expression}.Ticks)",
            out result);

    private static bool TryWriteDateOnly(ITypeSymbol type, string expression, int depth, out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsDateOnlyWireType(type),
            $"{DotBoxDRpcValueNames.GlobalKernelRpcValue}.Int32({expression}.DayNumber)",
            out result);

    private static bool TryWriteTimeOnly(ITypeSymbol type, string expression, int depth, out string result)
        => TryWriteWhen(
            DotBoxDRpcTypeMapper.IsTimeOnlyWireType(type),
            $"{DotBoxDRpcValueNames.GlobalKernelRpcValue}.Int64({expression}.Ticks)",
            out result);

    private static bool TryWriteIndex(ITypeSymbol type, string expression, int depth, out string result)
        => TryWriteWhen(DotBoxDRpcTypeMapper.IsIndexWireType(type), WriteIndexExpression(expression), out result);

    private static bool TryWriteRange(ITypeSymbol type, string expression, int depth, out string result)
        => TryWriteWhen(DotBoxDRpcTypeMapper.IsRangeWireType(type), WriteRangeExpression(expression), out result);

    private static bool TryWriteEnum(ITypeSymbol type, string expression, int depth, out string result)
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

    private static bool TryWriteList(ITypeSymbol type, string expression, int depth, out string result)
    {
        if (DotBoxDRpcTypeMapper.ListElementType(type) is { } elementType)
        {
            result = WriteListExpression(type, elementType, expression, depth);
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryWriteMap(ITypeSymbol type, string expression, int depth, out string result)
    {
        if (DotBoxDRpcTypeMapper.MapTypes(type) is { } map)
        {
            result = WriteMapExpression(type, map.Key, map.Value, expression, depth);
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryWriteRecord(ITypeSymbol type, string expression, int depth, out string result)
    {
        if (type is INamedTypeSymbol named && DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            result = WriteRecordExpression(named, expression, depth);
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
