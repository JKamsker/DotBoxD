using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;

namespace DotBoxD.Queryable.Authoring;

internal enum EventQueryNumericRouting
{
    None,
    Exact,
    Floating,
    Unresolved,
}

internal static class EventQueryNumericRoutingResolver
{
    public static EventQueryNumericRouting Resolve(Type eventType, string path, QueryValueKind literalKind)
    {
        if (!IsNumeric(literalKind))
        {
            return EventQueryNumericRouting.None;
        }

        Type memberType;
        try
        {
            memberType = MemberValueReader.ResolvePathType(eventType, path);
        }
        catch (InvalidOperationException)
        {
            return EventQueryNumericRouting.Unresolved;
        }

        memberType = Nullable.GetUnderlyingType(memberType) ?? memberType;
        var typeCode = Type.GetTypeCode(memberType);
        if (typeCode is TypeCode.Single or TypeCode.Double)
        {
            return EventQueryNumericRouting.Floating;
        }

        if (IsExactNumericType(typeCode))
        {
            return literalKind == QueryValueKind.Number ? EventQueryNumericRouting.Floating : EventQueryNumericRouting.Exact;
        }

        // A dynamically typed member cannot pick an exact/floating domain until it is read.
        // Keep this constraint in the full filter; other keys can still route the subscription.
        return EventQueryNumericRouting.Unresolved;
    }

    private static bool IsExactNumericType(TypeCode typeCode) => typeCode is
        TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16 or
        TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Decimal;

    public static bool IsNumeric(QueryValueKind kind) => kind is
        QueryValueKind.Integer or QueryValueKind.UnsignedInteger or QueryValueKind.Decimal or QueryValueKind.Number;
}

internal readonly record struct EventQueryRoutingPath(string Path, EventQueryNumericRouting NumericRouting);
