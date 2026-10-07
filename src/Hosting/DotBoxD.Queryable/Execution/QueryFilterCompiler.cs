using System.Linq.Expressions;
using System.Reflection;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Execution;

/// <summary>
/// Compiles a portable <see cref="QueryFilter"/> into a delegate for the hot-path tier. The compiled tree
/// specializes declared member paths and compatible scalar comparisons, falling back to the
/// <see cref="MemberValueReader"/> and <see cref="QueryValueComparer"/> primitives for other shapes.
/// Use it to promote frequently-evaluated filters; the interpreter remains the cold,
/// limit-checked default.
/// </summary>
public static class QueryFilterCompiler
{
    private static readonly MethodInfo ReadMethod =
        typeof(MemberValueReader).GetMethod(nameof(MemberValueReader.Read))!;

    private static readonly MethodInfo CompareMethod =
        typeof(QueryValueComparer).GetMethod(nameof(QueryValueComparer.Compare))!;

    private static readonly MethodInfo IsAnyEqualMethod =
        typeof(QueryValueComparer).GetMethod(nameof(QueryValueComparer.IsAnyEqual))!;

    /// <summary>Compiles <paramref name="filter"/> to a predicate over a (boxed) event object.</summary>
    public static Func<object, bool> Compile(QueryFilter filter, MemberValueReader reader)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(reader);
        QueryFilterInvariants.RequireValidShape(filter);
        var parameter = Expression.Parameter(typeof(object), "e");
        var body = Build(filter, parameter, reader);
        return Expression.Lambda<Func<object, bool>>(body, parameter).Compile();
    }

    private static Expression Build(QueryFilter filter, ParameterExpression target, MemberValueReader reader)
    {
        var kind = QueryFilterInvariants.RequireKnownKind(filter);
        return kind switch
        {
            QueryFilterKind.MatchAll => Expression.Constant(true),
            QueryFilterKind.And => Fold(filter.Children, target, reader, Expression.AndAlso, identity: true),
            QueryFilterKind.Or => Fold(filter.Children, target, reader, Expression.OrElse, identity: false),
            QueryFilterKind.Not => Expression.Not(Build(filter.Children[0], target, reader)),
            QueryFilterKind.Compare => CompareExpression(filter, target, reader),
            QueryFilterKind.In => InExpression(filter, target, reader),
            _ => throw new InvalidOperationException("Query filter compilation reached an unreachable kind."),
        };
    }

    private static Expression Fold(
        IReadOnlyList<QueryFilter> children,
        ParameterExpression target,
        MemberValueReader reader,
        Func<Expression, Expression, Expression> combine,
        bool identity)
    {
        Expression? accumulator = null;
        foreach (var child in children)
        {
            var compiled = Build(child, target, reader);
            accumulator = accumulator is null ? compiled : combine(accumulator, compiled);
        }

        return accumulator ?? Expression.Constant(identity);
    }

    private static Expression Read(QueryFilter filter, ParameterExpression target, MemberValueReader reader)
        => Expression.Call(Expression.Constant(reader), ReadMethod, target, Expression.Constant(filter.Field));

    private static Expression CompareExpression(QueryFilter filter, ParameterExpression target, MemberValueReader reader)
    {
        var expected = QueryFilterInvariants.CompareValue(filter);
        Expression Compare(Expression actual) => QueryScalarCompiler.TryBuild(actual, filter.Operator, expected, filter.IgnoreCase)
            ?? Expression.Call(CompareMethod, Expression.Convert(actual, typeof(object)), Expression.Constant(filter.Operator),
                Expression.Constant(expected), Expression.Constant(filter.IgnoreCase));
        var fallback = Compare(Read(filter, target, reader));
        return QueryMemberCompiler.TryBuild(reader, filter.Field, target, Compare, fallback) ?? fallback;
    }

    private static Expression InExpression(QueryFilter filter, ParameterExpression target, MemberValueReader reader)
    {
        var lookup = QueryMembershipLookup.TryCreate(filter);
        Expression Compare(Expression actual) => lookup is null
            ? Expression.Call(IsAnyEqualMethod, Expression.Convert(actual, typeof(object)),
                Expression.Constant(filter.Values, typeof(IReadOnlyList<QueryValue>)), Expression.Constant(filter.IgnoreCase))
            : Expression.Call(Expression.Constant(lookup), nameof(QueryMembershipLookup.Contains), Type.EmptyTypes,
                Expression.Convert(actual, typeof(object)));
        var fallback = Compare(Read(filter, target, reader));
        return QueryMemberCompiler.TryBuild(reader, filter.Field, target, Compare, fallback) ?? fallback;
    }
}
