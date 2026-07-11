using System.Linq.Expressions;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Translation;

/// <summary>
/// Translates the supported method-call predicates: ordinal string <c>Contains</c>/<c>StartsWith</c>/
/// <c>EndsWith</c>/<c>Equals</c> (with an optional <see cref="StringComparison"/> selecting case
/// sensitivity) against a constant, and <c>Contains</c> over a constant collection (lowered to
/// <see cref="QueryFilterKind.In"/>). The <c>makeValue</c> callback assigns capture ordinals.
/// </summary>
internal static class MethodCallFilterTranslator
{
    public static QueryFilter Translate(
        MethodCallExpression call,
        ParameterExpression parameter,
        Func<object?, Expression, QueryValue> makeValue)
    {
        if (StringMethodFilterTranslator.TryTranslate(call, parameter, makeValue, out var stringFilter))
        {
            return stringFilter;
        }

        if (ContainsMethodFilterTranslator.TryTranslate(call, parameter, out var inFilter))
        {
            return inFilter;
        }

        throw QueryTranslationException.Unsupported(
            call,
            "supported calls are string Contains/StartsWith/EndsWith/Equals and Contains over a constant collection.");
    }
}
