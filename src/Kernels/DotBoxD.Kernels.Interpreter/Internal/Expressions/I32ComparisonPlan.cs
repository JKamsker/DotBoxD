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
        I32CallEvaluator calls,
        out I32ComparisonPlan plan)
    {
        plan = default;
        if (expression is not BinaryExpression binary ||
            !Comparisons.TryGetValue(binary.Operator, out var op) ||
            !I32ExpressionPlan.TryCreate(binary.Left, frame, assumedInt32Local, calls, out var left) ||
            !I32ExpressionPlan.TryCreate(binary.Right, frame, assumedInt32Local, calls, out var right))
        {
            return false;
        }

        plan = new I32ComparisonPlan(op, left, right);
        return true;
    }

    private enum Comparison { Lt, Lte, Gt, Gte, Eq, Ne }
}
