using System.Linq.Expressions;

namespace DotBoxD.Queryable.Translation;

/// <summary>
/// Resolves a dotted member path (for example <c>AttackerId</c> or <c>Source.Id</c>) from a member-access
/// chain rooted at the query parameter, and detects whether an arbitrary subexpression touches that
/// parameter. Conversions (<c>(int)</c>, nullable lifts) are transparent.
/// </summary>
internal static class MemberPathReader
{
    /// <summary>
    /// Attempts to read the dotted path of a member chain rooted directly at <paramref name="parameter"/>.
    /// Returns <see langword="false"/> for any expression that is not a pure parameter-rooted member chain.
    /// </summary>
    public static bool TryReadPath(Expression expression, ParameterExpression parameter, out string path)
    {
        var segments = new Stack<string>();
        var current = StripConvert(expression);
        while (current is MemberExpression member)
        {
            segments.Push(member.Member.Name);
            current = StripConvert(member.Expression);
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
