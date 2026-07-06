using System.Globalization;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class HookChainIndexPredicateFormatting
{
    private static readonly IReadOnlyDictionary<SyntaxKind, (string Normal, string Reversed)> OperatorNames =
        new Dictionary<SyntaxKind, (string Normal, string Reversed)>
        {
            [SyntaxKind.EqualsExpression] = ("Equals", "Equals"),
            [SyntaxKind.NotEqualsExpression] = ("NotEquals", "NotEquals"),
            [SyntaxKind.GreaterThanExpression] = ("GreaterThan", "LessThan"),
            [SyntaxKind.GreaterThanOrEqualExpression] = ("GreaterThanOrEqual", "LessThanOrEqual"),
            [SyntaxKind.LessThanExpression] = ("LessThan", "GreaterThan"),
            [SyntaxKind.LessThanOrEqualExpression] = ("LessThanOrEqual", "GreaterThanOrEqual")
        };

    // Coerces the captured constant to the event property's manifest type and produces the C# literal the
    // emitter writes. Mirrors DotBoxDConstantExpressionLowerer's type rules.
    public static bool TryFormatValue(string propertyType, object? value, out string literal, out string valueType)
    {
        literal = string.Empty;
        valueType = propertyType;
        if (TryFormatBool(propertyType, value, out literal))
        {
            return true;
        }

        if (TryFormatInteger(propertyType, value, out literal))
        {
            return true;
        }

        if (TryFormatDouble(propertyType, value, out literal))
        {
            return true;
        }

        return TryFormatString(propertyType, value, out literal);
    }

    // Normalizes so the event property is always the left operand. The strings are the
    // DotBoxD.Plugins.IndexPredicateOperator member names verbatim.
    public static string OperatorName(SyntaxKind kind, bool constantOnLeft)
    {
        if (!OperatorNames.TryGetValue(kind, out var names))
        {
            throw new NotSupportedException();
        }

        return constantOnLeft ? names.Reversed : names.Normal;
    }

    private static bool TryFormatBool(string propertyType, object? value, out string literal)
    {
        if (propertyType == DotBoxDGenerationNames.ManifestTypes.Bool && value is bool boolean)
        {
            literal = boolean
                ? DotBoxDGenerationNames.CSharpLiterals.True
                : DotBoxDGenerationNames.CSharpLiterals.False;
            return true;
        }

        literal = string.Empty;
        return false;
    }

    private static bool TryFormatInteger(string propertyType, object? value, out string literal)
    {
        if (propertyType == DotBoxDGenerationNames.ManifestTypes.Int && value is int int32)
        {
            literal = int32.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (propertyType == DotBoxDGenerationNames.ManifestTypes.Long && value is int int64FromInt32)
        {
            literal = ((long)int64FromInt32).ToString(CultureInfo.InvariantCulture) +
                DotBoxDGenerationNames.CSharpLiterals.Int64Suffix;
            return true;
        }

        if (propertyType == DotBoxDGenerationNames.ManifestTypes.Long && value is long int64)
        {
            literal = int64.ToString(CultureInfo.InvariantCulture) +
                DotBoxDGenerationNames.CSharpLiterals.Int64Suffix;
            return true;
        }

        literal = string.Empty;
        return false;
    }

    private static bool TryFormatDouble(string propertyType, object? value, out string literal)
    {
        if (propertyType != DotBoxDGenerationNames.ManifestTypes.Double)
        {
            literal = string.Empty;
            return false;
        }

        if (value is int int32)
        {
            literal = Double(int32);
            return true;
        }

        if (value is long int64)
        {
            literal = Double(int64);
            return true;
        }

        if (value is double number && IsFinite(number))
        {
            literal = Double(number);
            return true;
        }

        literal = string.Empty;
        return false;
    }

    private static bool TryFormatString(string propertyType, object? value, out string literal)
    {
        if (propertyType == DotBoxDGenerationNames.ManifestTypes.String && value is string text)
        {
            literal = LiteralReader.StringLiteral(text);
            return true;
        }

        literal = string.Empty;
        return false;
    }

    private static string Double(double value)
        => value.ToString(DotBoxDGenerationNames.CSharpLiterals.DoubleRoundTripFormat, CultureInfo.InvariantCulture) +
           DotBoxDGenerationNames.CSharpLiterals.DoubleSuffix;

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
