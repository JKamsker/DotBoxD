using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal.Expressions;

// Unboxed i32 comparison (two i32 expression plans -> bool), used as the condition of a branched i32 loop body
// so the comparison avoids boxing its operands and result. FuelCost counts nodes identically to the compiler's
// per-subexpression metering (1 + left + right).
internal readonly struct I32ComparisonPlan
{
    private static readonly Dictionary<string, Comparison> Comparisons = new(StringComparer.Ordinal)
    {
        ["<"] = Comparison.Lt,
        ["<="] = Comparison.Lte,
        [">"] = Comparison.Gt,
        [">="] = Comparison.Gte,
        ["=="] = Comparison.Eq,
        ["!="] = Comparison.Ne
    };

    private readonly Comparison _op;
    private readonly I32ExpressionPlan _left;
    private readonly I32ExpressionPlan _right;

    private I32ComparisonPlan(Comparison op, I32ExpressionPlan left, I32ExpressionPlan right)
    {
        _op = op;
        _left = left;
        _right = right;
        FuelCost = 1 + left.FuelCost + right.FuelCost;
    }

    public int FuelCost { get; }

    public bool HasOnlyRawVariables()
        => _left.HasOnlyRawVariables() && _right.HasOnlyRawVariables();

    public void CollectRequiredRawSlots(List<int> slots)
    {
        _left.CollectRequiredRawSlots(slots);
        _right.CollectRequiredRawSlots(slots);
    }

    public bool Evaluate(InterpreterFrame frame, SandboxContext context)
    {
        var l = _left.Evaluate(frame, context);
        var r = _right.Evaluate(frame, context);
        return _op switch
        {
            Comparison.Lt => l < r,
            Comparison.Lte => l <= r,
            Comparison.Gt => l > r,
            Comparison.Gte => l >= r,
            Comparison.Eq => l == r,
            _ => l != r
        };
    }

    public static bool TryCreate(
        Expression expression,
        InterpreterFrame frame,
        string assumedInt32Local,
        out I32ComparisonPlan plan)
        => TryCreate(expression, frame, assumedInt32Local, calls: null, out plan);

    public static bool TryCreate(
        Expression expression,
        InterpreterFrame frame,
        string assumedInt32Local,
        I32CallEvaluator? calls,
        out I32ComparisonPlan plan)
    {
        plan = default;
        if (expression is not BinaryExpression binary ||
            !Comparisons.TryGetValue(binary.Operator, out var op) ||
            !TryCreateExpression(binary.Left, frame, assumedInt32Local, calls, out var left) ||
            !TryCreateExpression(binary.Right, frame, assumedInt32Local, calls, out var right))
        {
            return false;
        }

        plan = new I32ComparisonPlan(op, left, right);
        return true;
    }

    private static bool TryCreateExpression(
        Expression expression,
        InterpreterFrame frame,
        string assumedInt32Local,
        I32CallEvaluator? calls,
        out I32ExpressionPlan plan)
        => calls is null
            ? I32ExpressionPlan.TryCreate(expression, frame, assumedInt32Local, out plan)
            : I32ExpressionPlan.TryCreate(expression, frame, assumedInt32Local, calls, out plan);

    private enum Comparison { Lt, Lte, Gt, Gte, Eq, Ne }
}
