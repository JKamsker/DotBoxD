using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Translation;

/// <summary>
/// Evaluates the constant/closure side of a query expression into portable <see cref="QueryValue"/>s.
/// A captured local, field, or literal is resolved once at translation time (the expression is parameter-
/// free), so the wire model carries plain values rather than closure plumbing. Unsupported value types
/// raise a <see cref="QueryTranslationException"/> with an actionable message.
/// </summary>
internal static class QueryValueFactory
{
    /// <summary>
    /// Evaluates a parameter-free subexpression to a CLR object. Returns <see langword="false"/> when the
    /// expression references <paramref name="parameter"/> (and therefore is not a constant operand).
    /// </summary>
    public static bool TryEvaluateObject(Expression expression, ParameterExpression parameter, out object? result)
    {
        if (MemberPathReader.ReferencesParameter(expression, parameter))
        {
            result = null;
            return false;
        }

        // Conversions around a literal still need evaluation: they can round, truncate, throw, or call user code.
        if (expression is ConstantExpression constant)
        {
            result = constant.Value;
            return true;
        }

        try
        {
            // Compile the operand as-is (no manual Convert-to-object) and let DynamicInvoke box the
            // result; this is the robust partial-evaluation pattern used by LINQ providers.
            result = Expression.Lambda(expression).Compile().DynamicInvoke();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            throw new QueryTranslationException(
                $"Could not evaluate the constant operand '{expression}'.", ex);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is OperationCanceledException cancellation)
        {
            ExceptionDispatchInfo.Capture(cancellation).Throw();
            throw;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is { } inner)
        {
            throw new QueryTranslationException(
                $"Could not evaluate the constant operand '{expression}'.", inner);
        }
    }

    /// <summary>Converts a resolved CLR object into a <see cref="QueryValue"/>, throwing for unsupported types.</summary>
    public static QueryValue ToValue(object? raw, Expression source)
    {
        if (QueryValue.TryFromObject(raw, out var value))
        {
            return value;
        }

        if (raw is double d && !double.IsFinite(d) || raw is float f && !float.IsFinite(f))
        {
            throw new QueryTranslationException(
                $"Non-finite numeric values (NaN, Infinity) are not supported in '{source}'.");
        }

        throw new QueryTranslationException(
            $"Unsupported constant value type '{raw?.GetType().Name}' in '{source}'. " +
            "Only bool, integral and floating types, string, and enums are supported.");
    }

    /// <summary>Captures a collection once so comparer validation and enumeration inspect the same instance.</summary>
    public static IEnumerable EvaluateCollection(Expression expression, ParameterExpression parameter)
    {
        if (!TryEvaluateObject(expression, parameter, out var raw) || raw is not IEnumerable enumerable || raw is string)
        {
            throw QueryTranslationException.Unsupported(
                expression, "the 'in'/Contains operand must be a constant array or collection.");
        }

        return enumerable;
    }

}
