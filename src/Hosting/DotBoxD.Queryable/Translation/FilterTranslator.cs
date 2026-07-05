using System.Linq.Expressions;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Translation;

/// <summary>
/// Translates a predicate body (<c>Expression&lt;Func&lt;TEvent, bool&gt;&gt;</c>) into the portable
/// <see cref="QueryFilter"/> AST. Supported shapes: <c>&amp;&amp;</c>/<c>||</c>/<c>!</c>, the six comparison
/// operators against a constant, boolean member reads, ordinal string <c>Contains</c>/<c>StartsWith</c>/
/// <c>EndsWith</c>, and <c>Contains</c> over a constant collection (translated to
/// <see cref="QueryFilterKind.In"/>). Each captured literal is tagged with a stable <c>p0</c>, <c>p1</c>…
/// ordinal. Anything else throws <see cref="QueryTranslationException"/>.
/// </summary>
internal sealed class FilterTranslator(ParameterExpression parameter)
{
    private int _parameterIndex;

    /// <summary>Translates a predicate body into a filter AST.</summary>
    public QueryFilter Translate(Expression body)
    {
        var expression = MemberPathReader.StripConvert(body);
        if (TryTranslateLogical(expression, out var logical))
        {
            return logical;
        }

        if (TryTranslateComparisonExpression(expression, out var comparison))
        {
            return comparison;
        }

        return TranslateLeaf(expression);
    }

    private bool TryTranslateLogical(Expression expression, out QueryFilter filter)
    {
        filter = expression switch
        {
            BinaryExpression { NodeType: ExpressionType.AndAlso } b =>
                QueryFilter.And([Translate(b.Left), Translate(b.Right)]),
            BinaryExpression { NodeType: ExpressionType.OrElse } b =>
                QueryFilter.Or([Translate(b.Left), Translate(b.Right)]),
            UnaryExpression { NodeType: ExpressionType.Not } u =>
                QueryFilter.Not(Translate(u.Operand)),
            _ => null!
        };
        return filter is not null;
    }

    private bool TryTranslateComparisonExpression(Expression expression, out QueryFilter filter)
    {
        if (expression is BinaryExpression binary && TryMapOperator(binary.NodeType, out var op))
        {
            filter = TranslateComparison(binary, op);
            return true;
        }

        filter = null!;
        return false;
    }

    private QueryFilter TranslateLeaf(Expression expression)
    {
        return expression switch
        {
            MethodCallExpression call =>
                MethodCallFilterTranslator.Translate(call, parameter, MakeValue),
            MemberExpression member => TranslateBooleanMember(member),
            ConstantExpression { Value: bool literal } => literal ? QueryFilter.MatchAll : QueryFilter.Not(QueryFilter.MatchAll),
            _ => throw QueryTranslationException.Unsupported(expression),
        };
    }

    private QueryFilter TranslateComparison(BinaryExpression binary, QueryComparisonOperator op)
    {
        if (MemberPathReader.TryReadPath(binary.Left, parameter, out var leftPath) &&
            QueryValueFactory.TryEvaluateObject(binary.Right, parameter, out var rightRaw))
        {
            return QueryFilter.Compare(leftPath, op, MakeValue(rightRaw, binary.Right));
        }

        if (MemberPathReader.TryReadPath(binary.Right, parameter, out var rightPath) &&
            QueryValueFactory.TryEvaluateObject(binary.Left, parameter, out var leftRaw))
        {
            return QueryFilter.Compare(rightPath, Flip(op), MakeValue(leftRaw, binary.Left));
        }

        throw QueryTranslationException.Unsupported(
            binary, "one side of a comparison must be an event member and the other a constant.");
    }

    private QueryFilter TranslateBooleanMember(MemberExpression member)
    {
        if (member.Type == typeof(bool) && MemberPathReader.TryReadPath(member, parameter, out var path))
        {
            return QueryFilter.Compare(path, QueryComparisonOperator.Equal, QueryValue.FromBoolean(true));
        }

        throw QueryTranslationException.Unsupported(
            member, "a bare member predicate must be a boolean event member.");
    }

    private QueryValue MakeValue(object? raw, Expression source)
        => QueryValueFactory.ToValue(raw, source) with { ParameterKey = "p" + _parameterIndex++ };

    private static bool TryMapOperator(ExpressionType nodeType, out QueryComparisonOperator op)
    {
        op = nodeType switch
        {
            ExpressionType.Equal => QueryComparisonOperator.Equal,
            ExpressionType.NotEqual => QueryComparisonOperator.NotEqual,
            ExpressionType.GreaterThan => QueryComparisonOperator.GreaterThan,
            ExpressionType.GreaterThanOrEqual => QueryComparisonOperator.GreaterThanOrEqual,
            ExpressionType.LessThan => QueryComparisonOperator.LessThan,
            ExpressionType.LessThanOrEqual => QueryComparisonOperator.LessThanOrEqual,
            _ => (QueryComparisonOperator)(-1),
        };

        return (int)op >= 0;
    }

    private static QueryComparisonOperator Flip(QueryComparisonOperator op) => op switch
    {
        QueryComparisonOperator.GreaterThan => QueryComparisonOperator.LessThan,
        QueryComparisonOperator.GreaterThanOrEqual => QueryComparisonOperator.LessThanOrEqual,
        QueryComparisonOperator.LessThan => QueryComparisonOperator.GreaterThan,
        QueryComparisonOperator.LessThanOrEqual => QueryComparisonOperator.GreaterThanOrEqual,
        _ => op,
    };
}
