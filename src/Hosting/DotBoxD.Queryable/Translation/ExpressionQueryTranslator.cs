using System.Linq.Expressions;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Translation;

/// <summary>
/// The public entry point that turns authored LINQ expressions into the portable query AST. Each call
/// translates a single predicate or projection lambda; composing multiple predicates (for example several
/// <c>Where</c> calls) is the caller's responsibility via <see cref="QueryFilter.And"/>.
/// </summary>
public static class ExpressionQueryTranslator
{
    /// <summary>Translates a predicate lambda into a filter AST.</summary>
    public static QueryFilter TranslateFilter<TEvent>(Expression<Func<TEvent, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new FilterTranslator(predicate.Parameters[0]).Translate(predicate.Body);
    }

    /// <summary>Translates a projection lambda into a projection AST.</summary>
    public static QueryProjection TranslateProjection<TEvent, TProjection>(
        Expression<Func<TEvent, TProjection>> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new ProjectionTranslator(projection.Parameters[0]).Translate(projection.Body);
    }

    /// <summary>The fully-qualified portable name of an event type.</summary>
    public static string EventName<TEvent>() => typeof(TEvent).FullName ?? typeof(TEvent).Name;
}
