using DotBoxD.CodeGeneration.Shared.Defaults;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class LiteralReader
{
    public static string ParameterDefaultLiteral(IParameterSymbol parameter)
        => ParameterDefaultValueEmitter.ParameterDefaultClause(parameter, DefaultLiteralOptions.Analyzer);

    public static string ObjectDefaultLiteral(ITypeSymbol type, object? value)
        => CSharpLiteralFormatter.FormatValue(value, type, DefaultLiteralOptions.Analyzer) ??
           ObjectLiteral(value);

    public static object? DefaultObjectValue(
        ITypeSymbol type,
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (expression is not null)
        {
            var constant = semanticModel.GetConstantValue(expression, cancellationToken);
            if (!constant.HasValue)
            {
                throw new NotSupportedException("Live setting defaults must be compile-time constants.");
            }

            return constant.Value;
        }

        return type.SpecialType switch
        {
            SpecialType.System_Boolean => false,
            SpecialType.System_Int32 => 0,
            SpecialType.System_Int64 => 0L,
            SpecialType.System_Double => 0D,
            SpecialType.System_String => string.Empty,
            _ => null
        };
    }

    public static string ObjectLiteral(object? value)
        => NullOrBooleanLiteral(value) ??
           NumericLiteral(value) ??
           TextLiteral(value) ??
           Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ??
           DotBoxDGenerationNames.CSharpLiterals.Null;

    private static string? NullOrBooleanLiteral(object? value)
        => value switch
        {
            null => DotBoxDGenerationNames.CSharpLiterals.Null,
            bool boolean => boolean
                ? DotBoxDGenerationNames.CSharpLiterals.True
                : DotBoxDGenerationNames.CSharpLiterals.False,
            _ => null
        };

    private static string? NumericLiteral(object? value)
        => value switch
        {
            int number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            long number => number.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                DotBoxDGenerationNames.CSharpLiterals.Int64Suffix,
            double number => DoubleLiteral(number),
            decimal number => number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m",
            _ => null
        };

    private static string DoubleLiteral(double number)
    {
        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            throw new NotSupportedException("Double literal values must be finite.");
        }

        return number.ToString(
            DotBoxDGenerationNames.CSharpLiterals.DoubleRoundTripFormat,
            System.Globalization.CultureInfo.InvariantCulture) +
            DotBoxDGenerationNames.CSharpLiterals.DoubleSuffix;
    }

    private static string? TextLiteral(object? value)
        => value switch
        {
            char character => SymbolDisplay.FormatLiteral(character, quote: true),
            string text => SymbolDisplay.FormatLiteral(text, quote: true),
            _ => null
        };

    public static string StringLiteral(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

}
