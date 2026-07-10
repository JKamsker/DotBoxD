using System.Globalization;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Debugging;

internal static class SandboxDebugExpressionEvaluator
{
    public static PluginDebugEvaluationResult Evaluate(PluginDebugEvaluationRequest request)
    {
        if (request.AllowAwait)
        {
            return Failure("The SandboxOnly evaluator does not support await.");
        }

        try
        {
            var variables = Variables(request.Frame);
            return PluginDebugEvaluationResult.Success(
                new Parser(request.Expression, variables).Parse());
        }
        catch (SandboxDebugExpressionException exception)
        {
            return Failure(exception.Message);
        }
        catch (SandboxRuntimeException exception)
        {
            return PluginDebugEvaluationResult.Failure(exception.Error);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            return Failure(exception.Message);
        }
    }

    private static IReadOnlyDictionary<string, SandboxValue> Variables(ISandboxDebugFrame frame)
    {
        var variables = new Dictionary<string, SandboxValue>(StringComparer.Ordinal);
        foreach (var variable in frame.Arguments.Concat(frame.Locals))
        {
            if (variable.IsAssigned)
            {
                variables[variable.Name] = variable.Value!;
            }
        }

        return variables;
    }

    private static PluginDebugEvaluationResult Failure(string message)
        => PluginDebugEvaluationResult.Failure(new SandboxError(SandboxErrorCode.InvalidInput, message));

    private sealed class Parser
    {
        private readonly SandboxDebugExpressionTokenizer _tokens;
        private readonly IReadOnlyDictionary<string, SandboxValue> _variables;
        private SandboxDebugToken _current;

        public Parser(string expression, IReadOnlyDictionary<string, SandboxValue> variables)
        {
            _tokens = new SandboxDebugExpressionTokenizer(expression);
            _variables = variables;
            _current = _tokens.Next();
        }

        public SandboxValue Parse()
        {
            var value = ParseOr();
            if (_current.Kind != SandboxDebugTokenKind.End)
            {
                throw Invalid($"Unexpected token '{_current.Text}'.");
            }

            return value;
        }

        private SandboxValue ParseOr()
        {
            var value = ParseAnd();
            while (Take("||"))
            {
                var right = ParseAnd();
                value = SandboxValue.FromBool(Bool(value) || Bool(right));
            }

            return value;
        }

        private SandboxValue ParseAnd()
        {
            var value = ParseEquality();
            while (Take("&&"))
            {
                var right = ParseEquality();
                value = SandboxValue.FromBool(Bool(value) && Bool(right));
            }

            return value;
        }

        private SandboxValue ParseEquality()
        {
            var value = ParseComparison();
            while (_current is { Kind: SandboxDebugTokenKind.Operator, Text: "==" or "!=" })
            {
                var operation = Advance().Text;
                var right = ParseComparison();
                var equal = NumericEqual(value, right) ?? (value.Type.Equals(right.Type)
                    ? value.Equals(right)
                    : throw Invalid("Equality operands must have compatible sandbox types."));
                value = SandboxValue.FromBool(operation == "==" ? equal : !equal);
            }

            return value;
        }

        private static bool? NumericEqual(SandboxValue left, SandboxValue right)
            => (left, right) switch
            {
                (I32Value l, I32Value r) => l.Value == r.Value,
                (I32Value l, I64Value r) => l.Value == r.Value,
                (I64Value l, I32Value r) => l.Value == r.Value,
                (I64Value l, I64Value r) => l.Value == r.Value,
                (F64Value l, I32Value r) => l.Value == r.Value,
                (F64Value l, I64Value r) => l.Value == r.Value,
                (I32Value l, F64Value r) => l.Value == r.Value,
                (I64Value l, F64Value r) => l.Value == r.Value,
                (F64Value l, F64Value r) => l.Value == r.Value,
                _ => null
            };

        private SandboxValue ParseComparison()
        {
            var value = ParseTerm();
            while (_current is { Kind: SandboxDebugTokenKind.Operator, Text: "<" or "<=" or ">" or ">=" })
            {
                var operation = Advance().Text;
                value = operation switch
                {
                    "<" => SandboxNumericOperations.LessThan(value, ParseTerm()),
                    "<=" => SandboxNumericOperations.LessThanOrEqual(value, ParseTerm()),
                    ">" => SandboxNumericOperations.GreaterThan(value, ParseTerm()),
                    _ => SandboxNumericOperations.GreaterThanOrEqual(value, ParseTerm())
                };
            }

            return value;
        }

        private SandboxValue ParseTerm()
        {
            var value = ParseFactor();
            while (_current is { Kind: SandboxDebugTokenKind.Operator, Text: "+" or "-" })
            {
                var operation = Advance().Text;
                value = operation == "+"
                    ? SandboxNumericOperations.Add(value, ParseFactor())
                    : SandboxNumericOperations.Subtract(value, ParseFactor());
            }

            return value;
        }

        private SandboxValue ParseFactor()
        {
            var value = ParseUnary();
            while (_current is { Kind: SandboxDebugTokenKind.Operator, Text: "*" or "/" or "%" })
            {
                var operation = Advance().Text;
                value = operation switch
                {
                    "*" => SandboxNumericOperations.Multiply(value, ParseUnary()),
                    "/" => SandboxNumericOperations.Divide(value, ParseUnary()),
                    _ => SandboxNumericOperations.Remainder(value, ParseUnary())
                };
            }

            return value;
        }

        private SandboxValue ParseUnary()
        {
            if (Take("!"))
            {
                return SandboxValue.FromBool(!Bool(ParseUnary()));
            }

            return Take("-") ? SandboxNumericOperations.Negate(ParseUnary()) : ParsePrimary();
        }

        private SandboxValue ParsePrimary()
        {
            if (_current.Kind == SandboxDebugTokenKind.LeftParenthesis)
            {
                Advance();
                var value = ParseOr();
                Require(SandboxDebugTokenKind.RightParenthesis, ")");
                return value;
            }

            var token = Advance();
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
                return SandboxValue.FromDouble(double.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture));
            }

            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var i32)
                ? SandboxValue.FromInt32(i32)
                : SandboxValue.FromInt64(long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture));
        }

        private static bool Bool(SandboxValue value)
            => value is BoolValue boolean
                ? boolean.Value
                : throw Invalid("Logical operators require Bool operands.");

        private static SandboxDebugExpressionException Invalid(string message) => new(message);
    }
}
