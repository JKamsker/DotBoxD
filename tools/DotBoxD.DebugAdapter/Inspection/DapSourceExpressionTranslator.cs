using System.Text;
using System.Text.RegularExpressions;

namespace DotBoxD.DebugAdapter;

internal static class DapSourceExpressionTranslator
{
    public static string Translate(
        string expression,
        IReadOnlyList<DapSourceVariableBinding> bindings)
    {
        var writableBindings = bindings
            .Where(binding => binding.DisplayValue is null)
            .OrderByDescending(binding => binding.SourceName.Length)
            .ToArray();
        return Translate(expression, writableBindings);
    }

    private static string Translate(
        string expression,
        DapSourceVariableBinding[] bindings)
    {
        var translated = new StringBuilder(expression.Length);
        var segmentStart = 0;
        for (var position = 0; position < expression.Length; position++)
        {
            var delimiter = expression[position];
            if (delimiter is not ('"' or '\''))
            {
                continue;
            }

            AppendTranslatedSegment(translated, expression[segmentStart..position], bindings);
            var literalEnd = delimiter == '"' && IsInterpolated(expression, position)
                ? AppendInterpolatedLiteral(translated, expression, position, bindings)
                : AppendLiteral(translated, expression, position, delimiter);
            position = literalEnd - 1;
            segmentStart = literalEnd;
        }

        AppendTranslatedSegment(translated, expression[segmentStart..], bindings);
        return translated.ToString();
    }

    private static int AppendInterpolatedLiteral(
        StringBuilder destination,
        string expression,
        int openingQuote,
        DapSourceVariableBinding[] bindings)
    {
        var verbatim = IsVerbatim(expression, openingQuote);
        destination.Append('"');
        for (var position = openingQuote + 1; position < expression.Length; position++)
        {
            var current = expression[position];
            if (!verbatim && current == '\\' && position + 1 < expression.Length)
            {
                destination.Append(expression, position, 2);
                position++;
                continue;
            }

            if (current == '"')
            {
                if (verbatim && position + 1 < expression.Length && expression[position + 1] == '"')
                {
                    destination.Append("\"\"");
                    position++;
                    continue;
                }

                destination.Append('"');
                return position + 1;
            }

            if (current == '{' && position + 1 < expression.Length && expression[position + 1] == '{')
            {
                destination.Append("{{");
                position++;
                continue;
            }

            if (current == '{')
            {
                var interpolationEnd = FindInterpolationEnd(expression, position + 1);
                destination.Append('{');
                destination.Append(TranslateInterpolation(
                    expression[(position + 1)..interpolationEnd],
                    bindings));
                if (interpolationEnd < expression.Length)
                {
                    destination.Append('}');
                }

                position = interpolationEnd;
                continue;
            }

            destination.Append(current);
        }

        return expression.Length;
    }

    private static string TranslateInterpolation(
        string interpolation,
        DapSourceVariableBinding[] bindings)
    {
        var suffixStart = FindInterpolationSuffix(interpolation);
        return Translate(interpolation[..suffixStart], bindings) + interpolation[suffixStart..];
    }

    private static int FindInterpolationEnd(string expression, int start)
    {
        var nestedBraces = 0;
        for (var position = start; position < expression.Length; position++)
        {
            var delimiter = expression[position];
            if (delimiter is '"' or '\'')
            {
                position = FindLiteralEnd(
                    expression,
                    position,
                    delimiter,
                    delimiter == '"' && IsVerbatim(expression, position)) - 1;
            }
            else if (expression[position] == '{')
            {
                nestedBraces++;
            }
            else if (expression[position] == '}' && nestedBraces-- == 0)
            {
                return position;
            }
        }

        return expression.Length;
    }

    private static int FindInterpolationSuffix(string interpolation)
    {
        var nesting = 0;
        for (var position = 0; position < interpolation.Length; position++)
        {
            var delimiter = interpolation[position];
            if (delimiter is '"' or '\'')
            {
                position = FindLiteralEnd(
                    interpolation,
                    position,
                    delimiter,
                    delimiter == '"' && IsVerbatim(interpolation, position)) - 1;
                continue;
            }

            nesting += interpolation[position] switch
            {
                '(' or '[' or '{' => 1,
                ')' or ']' or '}' => -1,
                _ => 0
            };
            if (nesting == 0 && interpolation[position] is ',' or ':')
            {
                return position;
            }
        }

        return interpolation.Length;
    }

    private static int AppendLiteral(
        StringBuilder destination,
        string expression,
        int openingQuote,
        char delimiter)
    {
        var literalEnd = FindLiteralEnd(
            expression,
            openingQuote,
            delimiter,
            delimiter == '"' && IsVerbatim(expression, openingQuote));
        destination.Append(expression, openingQuote, literalEnd - openingQuote);
        return literalEnd;
    }

    private static int FindLiteralEnd(
        string expression,
        int openingQuote,
        char delimiter,
        bool verbatim)
    {
        for (var position = openingQuote + 1; position < expression.Length; position++)
        {
            if (!verbatim && expression[position] == '\\')
            {
                position++;
            }
            else if (expression[position] == delimiter)
            {
                if (verbatim && position + 1 < expression.Length && expression[position + 1] == delimiter)
                {
                    position++;
                    continue;
                }

                return position + 1;
            }
        }

        return expression.Length;
    }

    private static bool IsInterpolated(string expression, int openingQuote)
        => openingQuote > 0 && expression[openingQuote - 1] == '$' ||
            openingQuote > 1 && expression[openingQuote - 1] == '@' && expression[openingQuote - 2] == '$';

    private static bool IsVerbatim(string expression, int openingQuote)
        => openingQuote > 0 && expression[openingQuote - 1] == '@' ||
            openingQuote > 1 && expression[openingQuote - 1] == '$' && expression[openingQuote - 2] == '@';

    private static void AppendTranslatedSegment(
        StringBuilder destination,
        string segment,
        IReadOnlyList<DapSourceVariableBinding> bindings)
    {
        foreach (var binding in bindings)
        {
            segment = Regex.Replace(
                segment,
                $@"(?<![\w.]){Regex.Escape(binding.SourceName)}(?![\w.])",
                binding.SlotName,
                RegexOptions.CultureInvariant);
        }

        destination.Append(segment);
    }
}
