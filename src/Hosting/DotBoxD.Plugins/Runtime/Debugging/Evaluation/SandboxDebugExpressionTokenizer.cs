using System.Text;

namespace DotBoxD.Plugins.Debugging;

internal sealed class SandboxDebugExpressionTokenizer(string expression)
{
    private int _position;

    public SandboxDebugToken Next()
    {
        SkipWhiteSpace();
        if (_position == expression.Length)
        {
            return new SandboxDebugToken(SandboxDebugTokenKind.End, string.Empty);
        }

        var character = expression[_position];
        var literal = ReadLiteral(character);
        if (literal is not null)
        {
            return literal.Value;
        }

        foreach (var candidate in TwoCharacterOperators)
        {
            if (expression.AsSpan(_position).StartsWith(candidate, StringComparison.Ordinal))
            {
                _position += candidate.Length;
                return new SandboxDebugToken(SandboxDebugTokenKind.Operator, candidate);
            }
        }

        return SingleCharacterToken(character);
    }

    private SandboxDebugToken? ReadLiteral(char character)
    {
        if (char.IsAsciiLetter(character) || character == '_')
        {
            return Identifier();
        }

        if (char.IsAsciiDigit(character))
        {
            return Number();
        }

        return character == '"' ? String() : null;
    }

    private SandboxDebugToken SingleCharacterToken(char character)
    {
        _position++;
        if (character is '(' or ')')
        {
            var kind = character == '('
                ? SandboxDebugTokenKind.LeftParenthesis
                : SandboxDebugTokenKind.RightParenthesis;
            return new SandboxDebugToken(kind, character.ToString());
        }

        return "+-*/%!<>".Contains(character)
            ? new SandboxDebugToken(SandboxDebugTokenKind.Operator, character.ToString())
            : throw Invalid($"Unsupported character '{character}' at position {_position}.");
    }

    private SandboxDebugToken Identifier()
    {
        var start = _position++;
        while (_position < expression.Length &&
               (char.IsAsciiLetterOrDigit(expression[_position]) || expression[_position] == '_'))
        {
            _position++;
        }

        var text = expression[start.._position];
        return text switch
        {
            "true" or "false" => new SandboxDebugToken(SandboxDebugTokenKind.Boolean, text),
            _ => new SandboxDebugToken(SandboxDebugTokenKind.Identifier, text)
        };
    }

    private SandboxDebugToken Number()
    {
        var start = _position++;
        var decimalPoint = false;
        while (_position < expression.Length)
        {
            var character = expression[_position];
            if (char.IsAsciiDigit(character))
            {
                _position++;
                continue;
            }

            if (character == '.' && !decimalPoint)
            {
                decimalPoint = true;
                _position++;
                continue;
            }

            break;
        }

        return new SandboxDebugToken(SandboxDebugTokenKind.Number, expression[start.._position]);
    }

    private SandboxDebugToken String()
    {
        _position++;
        var builder = new StringBuilder();
        while (_position < expression.Length)
        {
            var character = expression[_position++];
            if (character == '"')
            {
                return new SandboxDebugToken(SandboxDebugTokenKind.String, builder.ToString());
            }

            if (character != '\\')
            {
                builder.Append(character);
                continue;
            }

            if (_position == expression.Length)
            {
                throw Invalid("The string literal ends with an escape character.");
            }

            builder.Append(Unescape(expression[_position++]));
        }

        throw Invalid("The string literal is not terminated.");
    }

    private static char Unescape(char character) => character switch
    {
        '"' => '"',
        '\\' => '\\',
        'n' => '\n',
        'r' => '\r',
        't' => '\t',
        _ => throw Invalid("The string literal contains an unsupported escape sequence.")
    };

    private void SkipWhiteSpace()
    {
        while (_position < expression.Length && char.IsWhiteSpace(expression[_position]))
        {
            _position++;
        }
    }

    private static readonly string[] TwoCharacterOperators = ["&&", "||", "==", "!=", "<=", ">="];

    private static SandboxDebugExpressionException Invalid(string message) => new(message);
}

internal readonly record struct SandboxDebugToken(SandboxDebugTokenKind Kind, string Text);

internal enum SandboxDebugTokenKind
{
    End,
    Identifier,
    Boolean,
    Number,
    String,
    Operator,
    LeftParenthesis,
    RightParenthesis
}

internal sealed class SandboxDebugExpressionException(string message) : Exception(message);
