using System.Globalization;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Debugging;

internal sealed class SandboxDebugExpressionParser
{
    private readonly SandboxDebugExpressionTokenizer _tokens;
    private readonly IReadOnlyDictionary<string, SandboxValue> _variables;
    private SandboxDebugToken _current;

    public SandboxDebugExpressionParser(
        string expression,
        IReadOnlyDictionary<string, SandboxValue> variables)
    {
        _tokens = new SandboxDebugExpressionTokenizer(expression);
        _variables = variables;
        _current = _tokens.Next();
    }

    public SandboxValue Parse()
    {
        var value = ParseOr(evaluate: true);
        if (_current.Kind != SandboxDebugTokenKind.End)
        {
            throw Invalid($"Unexpected token '{_current.Text}'.");
        }

        return value;
    }

    private SandboxValue ParseOr(bool evaluate)
    {
        var value = ParseAnd(evaluate);
        while (Take("||"))
        {
            var leftIsTrue = evaluate && Bool(value);
            var right = ParseAnd(evaluate && !leftIsTrue);
            if (evaluate)
            {
                value = SandboxValue.FromBool(leftIsTrue || Bool(right));
            }
        }

        return value;
    }

    private SandboxValue ParseAnd(bool evaluate)
    {
        var value = ParseEquality(evaluate);
        while (Take("&&"))
        {
            var leftIsTrue = evaluate && Bool(value);
            var right = ParseEquality(evaluate && leftIsTrue);
            if (evaluate)
            {
                value = SandboxValue.FromBool(leftIsTrue && Bool(right));
            }
        }

        return value;
    }

    private SandboxValue ParseEquality(bool evaluate)
    {
        var value = ParseComparison(evaluate);
        while (_current is { Kind: SandboxDebugTokenKind.Operator, Text: "==" or "!=" })
        {
            var operation = Advance().Text;
            var right = ParseComparison(evaluate);
            if (!evaluate)
            {
                continue;
            }

            (value, right) = PromoteNumerics(value, right);
            var equal = value.Type.Equals(right.Type)
                ? value.Equals(right)
                : throw Invalid("Equality operands must have compatible sandbox types.");
            value = SandboxValue.FromBool(operation == "==" ? equal : !equal);
        }

        return value;
    }

    private SandboxValue ParseComparison(bool evaluate)
    {
        var value = ParseTerm(evaluate);
        while (_current is { Kind: SandboxDebugTokenKind.Operator, Text: "<" or "<=" or ">" or ">=" })
        {
            var operation = Advance().Text;
            var right = ParseTerm(evaluate);
            if (!evaluate)
            {
                continue;
            }

            (value, right) = PromoteNumerics(value, right);
            value = operation switch
            {
                "<" => SandboxNumericOperations.LessThan(value, right),
                "<=" => SandboxNumericOperations.LessThanOrEqual(value, right),
                ">" => SandboxNumericOperations.GreaterThan(value, right),
                _ => SandboxNumericOperations.GreaterThanOrEqual(value, right)
            };
        }

        return value;
    }

    private SandboxValue ParseTerm(bool evaluate)
    {
        var value = ParseFactor(evaluate);
        while (_current is { Kind: SandboxDebugTokenKind.Operator, Text: "+" or "-" })
        {
            var operation = Advance().Text;
            var right = ParseFactor(evaluate);
            if (!evaluate)
            {
                continue;
            }

            (value, right) = PromoteNumerics(value, right);
            value = operation == "+"
                ? SandboxNumericOperations.Add(value, right)
                : SandboxNumericOperations.Subtract(value, right);
        }

        return value;
    }

    private SandboxValue ParseFactor(bool evaluate)
    {
        var value = ParseUnary(evaluate);
        while (_current is { Kind: SandboxDebugTokenKind.Operator, Text: "*" or "/" or "%" })
        {
            var operation = Advance().Text;
            var right = ParseUnary(evaluate);
            if (!evaluate)
            {
                continue;
            }

            (value, right) = PromoteNumerics(value, right);
            value = operation switch
            {
                "*" => SandboxNumericOperations.Multiply(value, right),
                "/" => SandboxNumericOperations.Divide(value, right),
                _ => SandboxNumericOperations.Remainder(value, right)
            };
        }

        return value;
    }

    private SandboxValue ParseUnary(bool evaluate)
    {
        if (Take("!"))
        {
            var operand = ParseUnary(evaluate);
            return evaluate ? SandboxValue.FromBool(!Bool(operand)) : SandboxValue.Unit;
        }

        if (Take("-"))
        {
            var operand = ParseUnary(evaluate);
            return evaluate ? SandboxNumericOperations.Negate(operand) : SandboxValue.Unit;
        }

        return ParsePrimary(evaluate);
    }

    private SandboxValue ParsePrimary(bool evaluate)
    {
        if (_current.Kind == SandboxDebugTokenKind.LeftParenthesis)
        {
            Advance();
            var value = ParseOr(evaluate);
            Require(SandboxDebugTokenKind.RightParenthesis, ")");
            return value;
        }

        var token = Advance();
        return evaluate ? EvaluatePrimary(token) : SkipPrimary(token);
    }

    private SandboxValue EvaluatePrimary(SandboxDebugToken token)
    {
        return token.Kind switch
        {
            SandboxDebugTokenKind.Identifier => _variables.TryGetValue(token.Text, out var value)
                ? value
                : throw Invalid($"Unknown or unassigned sandbox variable '{token.Text}'."),
            SandboxDebugTokenKind.Boolean => SandboxValue.FromBool(token.Text == "true"),
            SandboxDebugTokenKind.String => SandboxValue.FromString(token.Text),
            SandboxDebugTokenKind.Number => Number(token.Text),
            _ => throw Invalid($"Expected a sandbox value but found '{token.Text}'.")
        };
    }

    private static SandboxValue SkipPrimary(SandboxDebugToken token)
        => token.Kind is SandboxDebugTokenKind.Identifier or
            SandboxDebugTokenKind.Boolean or SandboxDebugTokenKind.String or SandboxDebugTokenKind.Number
            ? SandboxValue.Unit
            : throw Invalid($"Expected a sandbox value but found '{token.Text}'.");

    private static (SandboxValue Left, SandboxValue Right) PromoteNumerics(
        SandboxValue left,
        SandboxValue right)
    {
        if (!IsNumeric(left) || !IsNumeric(right))
        {
            return (left, right);
        }

        if (left is F64Value || right is F64Value)
        {
            return (SandboxValue.FromDouble(ToDouble(left)), SandboxValue.FromDouble(ToDouble(right)));
        }

        return left is I64Value || right is I64Value
            ? (SandboxValue.FromInt64(ToInt64(left)), SandboxValue.FromInt64(ToInt64(right)))
            : (left, right);
    }

    private SandboxDebugToken Advance()
    {
        var previous = _current;
        _current = _tokens.Next();
        return previous;
    }

    private bool Take(string operation)
    {
        if (_current.Kind != SandboxDebugTokenKind.Operator || _current.Text != operation)
        {
            return false;
        }

        Advance();
        return true;
    }

    private void Require(SandboxDebugTokenKind kind, string text)
    {
        if (_current.Kind != kind)
        {
            throw Invalid($"Expected '{text}'.");
        }

        Advance();
    }

    private static SandboxValue Number(string text)
    {
        if (text.Contains('.', StringComparison.Ordinal))
        {
            return SandboxValue.FromDouble(
                double.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture));
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var i32)
            ? SandboxValue.FromInt32(i32)
            : SandboxValue.FromInt64(long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture));
    }

    private static bool IsNumeric(SandboxValue value) => value is I32Value or I64Value or F64Value;

    private static long ToInt64(SandboxValue value) => value is I32Value i32 ? i32.Value : ((I64Value)value).Value;

    private static double ToDouble(SandboxValue value) => value switch
    {
        I32Value i32 => i32.Value,
        I64Value i64 => i64.Value,
        F64Value f64 => f64.Value,
        _ => throw Invalid("Numeric promotion requires numeric operands.")
    };

    private static bool Bool(SandboxValue value)
        => value is BoolValue boolean
            ? boolean.Value
            : throw Invalid("Logical operators require Bool operands.");

    private static SandboxDebugExpressionException Invalid(string message) => new(message);
}
