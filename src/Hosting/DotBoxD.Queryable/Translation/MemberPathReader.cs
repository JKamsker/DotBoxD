using System.Linq.Expressions;

namespace DotBoxD.Queryable.Translation;

/// <summary>
/// Resolves a dotted member path (for example <c>AttackerId</c> or <c>Source.Id</c>) from a member-access
/// chain rooted at the query parameter, and detects whether an arbitrary subexpression touches that
/// parameter. Only conversions that preserve member-path semantics are transparent.
/// </summary>
internal static class MemberPathReader
{
    private static readonly HashSet<(TypeCode Source, TypeCode Target)> ExactNumericWidenings =
    [
        (TypeCode.SByte, TypeCode.Int16),
        (TypeCode.SByte, TypeCode.Int32),
        (TypeCode.SByte, TypeCode.Int64),
        (TypeCode.SByte, TypeCode.Single),
        (TypeCode.SByte, TypeCode.Double),
        (TypeCode.SByte, TypeCode.Decimal),
        (TypeCode.Byte, TypeCode.Int16),
        (TypeCode.Byte, TypeCode.UInt16),
        (TypeCode.Byte, TypeCode.Int32),
        (TypeCode.Byte, TypeCode.UInt32),
        (TypeCode.Byte, TypeCode.Int64),
        (TypeCode.Byte, TypeCode.UInt64),
        (TypeCode.Byte, TypeCode.Single),
        (TypeCode.Byte, TypeCode.Double),
        (TypeCode.Byte, TypeCode.Decimal),
        (TypeCode.Int16, TypeCode.Int32),
        (TypeCode.Int16, TypeCode.Int64),
        (TypeCode.Int16, TypeCode.Single),
        (TypeCode.Int16, TypeCode.Double),
        (TypeCode.Int16, TypeCode.Decimal),
        (TypeCode.UInt16, TypeCode.Int32),
        (TypeCode.UInt16, TypeCode.UInt32),
        (TypeCode.UInt16, TypeCode.Int64),
        (TypeCode.UInt16, TypeCode.UInt64),
        (TypeCode.UInt16, TypeCode.Single),
        (TypeCode.UInt16, TypeCode.Double),
        (TypeCode.UInt16, TypeCode.Decimal),
        (TypeCode.Int32, TypeCode.Int64),
        (TypeCode.Int32, TypeCode.Double),
        (TypeCode.Int32, TypeCode.Decimal),
        (TypeCode.UInt32, TypeCode.Int64),
        (TypeCode.UInt32, TypeCode.UInt64),
        (TypeCode.UInt32, TypeCode.Double),
        (TypeCode.UInt32, TypeCode.Decimal),
        (TypeCode.Int64, TypeCode.Decimal),
        (TypeCode.UInt64, TypeCode.Decimal),
        (TypeCode.Single, TypeCode.Double),
    ];

    /// <summary>
    /// Attempts to read the dotted path of a member chain rooted directly at <paramref name="parameter"/>.
    /// Returns <see langword="false"/> for any expression that is not a pure parameter-rooted member chain.
    /// </summary>
    public static bool TryReadPath(Expression expression, ParameterExpression parameter, out string path)
    {
        var segments = new Stack<string>();
        var current = StripPathConvert(expression, parameter);
        while (current is MemberExpression member)
        {
            if (IsNullableValueMember(member))
            {
                throw QueryTranslationException.Unsupported(
                    member,
                    "Nullable<T>.HasValue and Nullable<T>.Value cannot be represented as portable query paths; compare the nullable member directly to null or a concrete value.");
            }

            segments.Push(member.Member.Name);
            current = StripPathConvert(member.Expression, parameter);
        }

        if (current == parameter && segments.Count > 0)
        {
            path = string.Join('.', segments);
            return true;
        }

        path = string.Empty;
        return false;
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="expression"/> references <paramref name="parameter"/>.</summary>
    public static bool ReferencesParameter(Expression expression, ParameterExpression parameter)
    {
        var finder = new ParameterFinder(parameter);
        finder.Visit(expression);
        return finder.Found;
    }

    /// <summary>Removes transparent <see cref="ExpressionType.Convert"/>/<see cref="ExpressionType.ConvertChecked"/> wrappers.</summary>
    public static Expression StripConvert(Expression? expression)
    {
        var current = expression;
        while (current is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            current = unary.Operand;
        }

        return current ?? throw new ArgumentNullException(nameof(expression));
    }

    /// <summary>
    /// Removes conversions that are safe to ignore while reading a member path. Lossy/member-changing casts
    /// over the query parameter are rejected instead of silently lowering the wrong path semantics.
    /// </summary>
    public static Expression StripPathConvert(Expression? expression, ParameterExpression parameter)
    {
        var current = expression ?? throw new ArgumentNullException(nameof(expression));
        while (current is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            if (!IsTransparentPathConversion(unary.Operand.Type, unary.Type))
            {
                if (ReferencesParameter(unary.Operand, parameter))
                {
                    throw QueryTranslationException.Unsupported(
                        unary,
                        "casts on event member paths are not supported because they can change query semantics; compare the member directly.");
                }

                return current;
            }

            current = unary.Operand;
        }

        return current;
    }

    private static bool IsNullableValueMember(MemberExpression member) =>
        member.Expression?.Type is { } declaringType &&
        Nullable.GetUnderlyingType(declaringType) is not null &&
        (member.Member.Name == "HasValue" || member.Member.Name == "Value");

    private static bool IsTransparentPathConversion(Type source, Type target)
    {
        if (source == target)
        {
            return true;
        }

        var nullableSource = Nullable.GetUnderlyingType(source);
        var nullableTarget = Nullable.GetUnderlyingType(target);

        if (nullableSource is not null && nullableTarget is null)
        {
            return false;
        }

        var sourceValue = nullableSource ?? source;
        var targetValue = nullableTarget ?? target;
        if (sourceValue == targetValue)
        {
            return true;
        }

        return IsExactNumericWidening(sourceValue, targetValue);
    }

    private static bool IsExactNumericWidening(Type source, Type target)
        => ExactNumericWidenings.Contains((Type.GetTypeCode(source), Type.GetTypeCode(target)));

    private sealed class ParameterFinder(ParameterExpression parameter) : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == parameter)
            {
                Found = true;
            }

            return base.VisitParameter(node);
        }
    }
}
