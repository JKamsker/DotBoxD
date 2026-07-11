using DotBoxD.Kernels;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Abstractions;

/// <summary>
/// Builds sandbox IR expressions for hand-authored lowered pipeline steps.
/// </summary>
public sealed class IRExpressionBuilder
{
    public const string CurrentParameterName = "$dotboxd.current";

    public IRExpressionBuilder()
        : this(new SourceSpan(1, 1))
    {
    }

    public IRExpressionBuilder(SourceSpan span)
    {
        Span = span ?? throw new ArgumentNullException(nameof(span));
    }

    public SourceSpan Span { get; }

    public VariableExpression Current()
        => Variable(CurrentParameterName);

    public VariableExpression Variable(string name)
        => new(RequireName(name, nameof(name)), Span);

    public LiteralExpression Literal(SandboxValue value)
        => new(value ?? throw new ArgumentNullException(nameof(value)), Span);

    public LiteralExpression Bool(bool value)
        => Literal(SandboxValue.FromBool(value));

    public LiteralExpression Int32(int value)
        => Literal(SandboxValue.FromInt32(value));

    public LiteralExpression Int64(long value)
        => Literal(SandboxValue.FromInt64(value));

    public LiteralExpression Double(double value)
        => Literal(SandboxValue.FromDouble(value));

    public LiteralExpression String(string value)
        => Literal(SandboxValue.FromString(value));

    public LiteralExpression Guid(Guid value)
        => Literal(SandboxValue.FromGuid(value));

    public CallExpression Field(int index)
        => RecordGet(Current(), index);

    public CallExpression RecordGet(Expression record, int index)
        => Call("record.get", RequireExpression(record, nameof(record)), Int32(index));

    public CallExpression Record(params Expression[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return Call("record.new", fields);
    }

    public CallExpression ListCount(Expression list)
        => Call("list.count", RequireExpression(list, nameof(list)));

    public CallExpression ListGet(Expression list, Expression index)
        => Call(
            "list.get",
            RequireExpression(list, nameof(list)),
            RequireExpression(index, nameof(index)));

    public CallExpression MapGet(Expression map, Expression key)
        => Call(
            "map.get",
            RequireExpression(map, nameof(map)),
            RequireExpression(key, nameof(key)));

    public CallExpression StringLength(Expression text)
        => Call("string.length", RequireExpression(text, nameof(text)));

    public UnaryExpression Not(Expression value)
        => Unary("!", value);

    public UnaryExpression Negate(Expression value)
        => Unary("-", value);

    public BinaryExpression And(Expression left, Expression right)
        => Binary(left, "&&", right);

    public BinaryExpression Or(Expression left, Expression right)
        => Binary(left, "||", right);

    public BinaryExpression Equal(Expression left, Expression right)
        => Binary(left, "==", right);

    public BinaryExpression NotEqual(Expression left, Expression right)
        => Binary(left, "!=", right);

    public BinaryExpression GreaterThan(Expression left, Expression right)
        => Binary(left, ">", right);

    public BinaryExpression GreaterThanOrEqual(Expression left, Expression right)
        => Binary(left, ">=", right);

    public BinaryExpression LessThan(Expression left, Expression right)
        => Binary(left, "<", right);

    public BinaryExpression LessThanOrEqual(Expression left, Expression right)
        => Binary(left, "<=", right);

    public BinaryExpression Add(Expression left, Expression right)
        => Binary(left, "+", right);

    public BinaryExpression Subtract(Expression left, Expression right)
        => Binary(left, "-", right);

    public BinaryExpression Multiply(Expression left, Expression right)
        => Binary(left, "*", right);

    public BinaryExpression Divide(Expression left, Expression right)
        => Binary(left, "/", right);

    public BinaryExpression Remainder(Expression left, Expression right)
        => Binary(left, "%", right);

    public UnaryExpression Unary(string @operator, Expression value)
        => new(RequireName(@operator, nameof(@operator)), RequireExpression(value, nameof(value)), Span);

    public BinaryExpression Binary(Expression left, string @operator, Expression right)
        => new(
            RequireExpression(left, nameof(left)),
            RequireName(@operator, nameof(@operator)),
            RequireExpression(right, nameof(right)),
            Span);

    public CallExpression Call(string name, params Expression[] arguments)
        => Call(name, (IReadOnlyList<Expression>)arguments);

    public CallExpression Call(string name, IReadOnlyList<Expression> arguments, SandboxType? genericType = null)
        => new(RequireName(name, nameof(name)), CopyArguments(arguments), genericType, Span);

    private static string RequireName(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value must not be empty.", paramName);
        }

        return value;
    }

    private static Expression RequireExpression(Expression expression, string paramName)
        => expression ?? throw new ArgumentNullException(paramName);

    private static Expression[] CopyArguments(IReadOnlyList<Expression> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var copy = new Expression[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
        {
            copy[i] = arguments[i] ?? throw new ArgumentException(
                "Call arguments must not contain null values.",
                nameof(arguments));
        }

        return copy;
    }
}
