using System.Linq.Expressions;

namespace DotBoxD.Queryable.Translation;

/// <summary>
/// Thrown when an authored <c>Where</c>/<c>Select</c> expression uses a shape the portable query model does
/// not support. The message names the offending expression and explains why it was rejected, so a plugin
/// author gets an actionable diagnostic at registration time rather than a silent or partial translation.
/// </summary>
public sealed class QueryTranslationException : Exception
{
    /// <summary>Creates an exception with an explanatory <paramref name="message"/>.</summary>
    public QueryTranslationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a <paramref name="message"/> and inner cause.</summary>
    public QueryTranslationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Builds an exception describing an unsupported <paramref name="expression"/> with an optional reason.</summary>
    public static QueryTranslationException Unsupported(Expression expression, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var detail = string.IsNullOrEmpty(reason) ? string.Empty : $" {reason}";
        return new QueryTranslationException(
            $"Unsupported query expression '{expression}' (node '{expression.NodeType}').{detail}");
    }
}
