using System.Text;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Queryable.Text;

internal enum QueryTokenKind
{
    Word,
    String,
    Number,
    Symbol,
    End,
}

internal readonly record struct QueryToken(QueryTokenKind Kind, string Text);

/// <summary>Lexes the query text DSL into tokens; raises <see cref="QueryTranslationException"/> on bad input.</summary>
internal static class QueryTextTokenizer
{
    public static IReadOnlyList<QueryToken> Tokenize(string text)
    {
        var tokens = new List<QueryToken>();
        var index = 0;
        while (index < text.Length)
        {
            var c = text[index];
            if (char.IsWhiteSpace(c))
            {
                index++;
                continue;
            }

            tokens.Add(LexToken(text, ref index));
        }

        tokens.Add(new QueryToken(QueryTokenKind.End, string.Empty));
        return tokens;
    }

    private static QueryToken LexToken(string text, ref int index)
    {
        var c = text[index];
        if (IsSingleCharacterSymbol(c))
        {
            index++;
            return new QueryToken(QueryTokenKind.Symbol, c.ToString());
        }

        if (TryLexOperator(text, ref index, out var op))
        {
            return new QueryToken(QueryTokenKind.Symbol, op);
        }

        if (c == '"')
        {
            return new QueryToken(QueryTokenKind.String, LexString(text, ref index));
        }

        if (CanStartNumber(c))
        {
            return new QueryToken(QueryTokenKind.Number, LexNumber(text, ref index));
        }

        if (CanStartWord(c))
        {
            return new QueryToken(QueryTokenKind.Word, LexWord(text, ref index));
        }

        throw new QueryTranslationException($"Unexpected character '{c}' at position {index} in query text.");
    }

    private static bool IsSingleCharacterSymbol(char c)
        => c is '(' or ')' or '[' or ']' or ',' or '*' or '~';

    private static bool CanStartNumber(char c)
        => c == '-' || char.IsDigit(c);

    private static bool CanStartWord(char c)
        => char.IsLetter(c) || c == '_';

    private static bool TryLexOperator(string text, ref int index, out string op)
    {
        var c = text[index];
        var next = index + 1 < text.Length ? text[index + 1] : '\0';
        if (IsTwoCharacterOperator(c, next))
        {
            op = text.Substring(index, 2);
            index += 2;
            return true;
        }

        if (IsSingleCharacterOperator(c))
        {
            op = c.ToString();
            index++;
            return true;
        }

        if (c == '=')
        {
            throw new QueryTranslationException($"Unexpected '=' at position {index}; use '==' for equality.");
        }

        op = string.Empty;
        return false;
    }

    private static bool IsTwoCharacterOperator(char c, char next)
        => c switch
        {
            '&' => next == '&',
            '|' => next == '|',
            '=' or '!' or '>' or '<' => next == '=',
            _ => false,
        };

    private static bool IsSingleCharacterOperator(char c)
        => c is '!' or '>' or '<';

    private static string LexString(string text, ref int index)
    {
        var builder = new StringBuilder();
        index++; // opening quote
        while (index < text.Length)
        {
            var c = text[index++];
            if (c == '\\' && index < text.Length)
            {
                var escaped = text[index++];
                if (escaped is not ('"' or '\\'))
                {
                    throw new QueryTranslationException(
                        $"Unsupported string escape '\\{escaped}' at position {index - 2}; only '\\\"' and '\\\\' are supported.");
                }

                builder.Append(escaped);
                continue;
            }

            if (c == '\\')
            {
                throw new QueryTranslationException("Unterminated string escape in query text.");
            }

            if (c == '"')
            {
                return builder.ToString();
            }

            builder.Append(c);
        }

        throw new QueryTranslationException("Unterminated string literal in query text.");
    }

    // Numbers: optional leading '-', digits/'.', and an optional well-formed exponent (e/E [+/-] digits) so
    // round-trip ("R") doubles such as 1E+21 or 1.5E-10 re-tokenize as a single Number.
    private static string LexNumber(string text, ref int index)
    {
        var start = index;
        ConsumeLeadingMinus(text, ref index);
        ConsumeNumberBody(text, ref index);
        ConsumeExponent(text, ref index);
        ConsumeNumericSuffix(text, ref index);
        return text[start..index];
    }

    private static void ConsumeLeadingMinus(string text, ref int index)
    {
        if (text[index] == '-')
        {
            index++;
        }
    }

    private static void ConsumeNumberBody(string text, ref int index)
    {
        while (index < text.Length && (char.IsDigit(text[index]) || text[index] == '.'))
        {
            index++;
        }
    }

    private static void ConsumeExponent(string text, ref int index)
    {
        if (index >= text.Length || text[index] is not ('e' or 'E'))
        {
            return;
        }

        var exponent = index + 1;
        ConsumeExponentSign(text, ref exponent);
        if (exponent < text.Length && char.IsDigit(text[exponent]))
        {
            index = exponent + 1;
            ConsumeExponentDigits(text, ref index);
        }
    }

    private static void ConsumeExponentSign(string text, ref int exponent)
    {
        if (exponent < text.Length && text[exponent] is '+' or '-')
        {
            exponent++;
        }
    }

    private static void ConsumeExponentDigits(string text, ref int index)
    {
        while (index < text.Length && char.IsDigit(text[index]))
        {
            index++;
        }
    }

    private static void ConsumeNumericSuffix(string text, ref int index)
    {
        // Typed numeric suffix: 'm'/'M' (decimal) or 'u'/'U' (unsigned). Consume one into the token so the
        // parser can disambiguate it (e.g. 1.10m -> decimal, 42u -> ulong). Guard against eating into a word.
        if (index < text.Length && text[index] is 'm' or 'M' or 'u' or 'U' && !StartsWordAfterSuffix(text, index))
        {
            index++;
        }
    }

    private static bool StartsWordAfterSuffix(string text, int index)
    {
        var after = index + 1;
        return after < text.Length && (char.IsLetterOrDigit(text[after]) || text[after] == '_');
    }

    private static string LexWord(string text, ref int index)
    {
        var start = index;
        while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] is '_' or '.'))
        {
            index++;
        }

        return text[start..index];
    }
}
