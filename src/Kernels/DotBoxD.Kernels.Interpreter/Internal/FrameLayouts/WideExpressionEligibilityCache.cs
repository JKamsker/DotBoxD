using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal enum WideExpressionKind
{
    Unsupported,
    I64,
    F64
}

internal abstract class WideExpressionAdmissionState
{
    public static WideExpressionAdmissionState Observed { get; } = new ObservedState();

    private sealed class ObservedState : WideExpressionAdmissionState
    {
    }
}

/// <summary>
/// Keeps a few whole-tree eligibility results with each prepared plan. Entries
/// describe only immutable expression shape and layout slot kinds; invocation-
/// specific assignment state is checked again before every use.
/// </summary>
internal sealed class WideExpressionEligibilityCache : WideExpressionAdmissionState
{
    private const int Capacity = 4;
    private const int MaxClassifiedNodes = 512;
    private const int MaxRequiredSlots = 64;
    private readonly Entry?[] _entries = new Entry?[Capacity];

    public bool TryGetKind(
        Expression expression,
        InterpreterFrame frame,
        out WideExpressionKind kind)
    {
        var layout = frame.Layout;
        var hasCapacity = false;
        for (var i = 0; i < _entries.Length; i++)
        {
            var entry = Volatile.Read(ref _entries[i]);
            if (entry is null)
            {
                hasCapacity = true;
                continue;
            }

            if (entry.Matches(layout, expression))
            {
                kind = entry.Kind;
                return entry.CanRun(frame);
            }
        }

        if (!hasCapacity)
        {
            kind = WideExpressionKind.Unsupported;
            return false;
        }

        var classified = Classify(layout, expression);
        Store(classified);
        kind = classified.Kind;
        return classified.CanRun(frame);
    }

    private void Store(Entry entry)
    {
        for (var i = 0; i < _entries.Length; i++)
        {
            var existing = Volatile.Read(ref _entries[i]);
            if (existing?.Matches(entry.Layout, entry.Expression) == true)
            {
                return;
            }

            if (existing is not null)
            {
                continue;
            }

            var raced = Interlocked.CompareExchange(ref _entries[i], entry, null);
            if (raced is null || raced.Matches(entry.Layout, entry.Expression))
            {
                return;
            }
        }
    }

    private static Entry Classify(
        FunctionFrameLayout layout,
        Expression expression)
    {
        HashSet<int>? requiredSlots = null;
        var classifiedNodes = 0;
        var kind = ClassifyExpression(
            expression,
            layout,
            ref requiredSlots,
            ref classifiedNodes);
        var slots = kind == WideExpressionKind.Unsupported
            ? []
            : requiredSlots?.ToArray() ?? [];
        return new Entry(layout, expression, kind, slots);
    }

    private static WideExpressionKind ClassifyExpression(
        Expression expression,
        FunctionFrameLayout layout,
        ref HashSet<int>? requiredSlots,
        ref int classifiedNodes)
    {
        classifiedNodes++;
        if (classifiedNodes > MaxClassifiedNodes)
        {
            return WideExpressionKind.Unsupported;
        }

        return expression switch
        {
            LiteralExpression literal => ClassifyLiteral(literal),
            VariableExpression variable => ClassifyVariable(variable, layout, ref requiredSlots),
            UnaryExpression { Operator: "-" } unary =>
                ClassifyExpression(
                    unary.Operand,
                    layout,
                    ref requiredSlots,
                    ref classifiedNodes),
            BinaryExpression binary => ClassifyBinary(
                binary,
                layout,
                ref requiredSlots,
                ref classifiedNodes),
            _ => WideExpressionKind.Unsupported
        };
    }

    private static WideExpressionKind ClassifyLiteral(LiteralExpression literal)
    {
        if (literal.Value is I64Value)
        {
            return WideExpressionKind.I64;
        }

        return literal.Value is F64Value
            ? WideExpressionKind.F64
            : WideExpressionKind.Unsupported;
    }

    private static WideExpressionKind ClassifyVariable(
        VariableExpression variable,
        FunctionFrameLayout layout,
        ref HashSet<int>? requiredSlots)
    {
        if (!layout.TryGetSlot(variable.Name, out var slot))
        {
            return WideExpressionKind.Unsupported;
        }

        var kind = layout.IsI64Slot(slot)
            ? WideExpressionKind.I64
            : layout.IsF64Slot(slot) ? WideExpressionKind.F64 : WideExpressionKind.Unsupported;
        if (kind != WideExpressionKind.Unsupported)
        {
            return AddRequiredSlot(slot, ref requiredSlots)
                ? kind
                : WideExpressionKind.Unsupported;
        }

        return kind;
    }

    private static WideExpressionKind ClassifyBinary(
        BinaryExpression binary,
        FunctionFrameLayout layout,
        ref HashSet<int>? requiredSlots,
        ref int classifiedNodes)
    {
        if (!IsArithmeticOperator(binary.Operator))
        {
            return WideExpressionKind.Unsupported;
        }

        var left = ClassifyExpression(
            binary.Left,
            layout,
            ref requiredSlots,
            ref classifiedNodes);
        if (left == WideExpressionKind.Unsupported)
        {
            return left;
        }

        var right = ClassifyExpression(
            binary.Right,
            layout,
            ref requiredSlots,
            ref classifiedNodes);
        return left == right ? left : WideExpressionKind.Unsupported;
    }

    private static bool IsArithmeticOperator(string op)
        => op is "+" or "-" or "*" or "/" or "%";

    private static bool AddRequiredSlot(int slot, ref HashSet<int>? requiredSlots)
    {
        requiredSlots ??= [];
        if (requiredSlots.Contains(slot))
        {
            return true;
        }

        return requiredSlots.Count < MaxRequiredSlots && requiredSlots.Add(slot);
    }

    private sealed class Entry(
        FunctionFrameLayout layout,
        Expression expression,
        WideExpressionKind kind,
        int[] requiredSlots)
    {
        public FunctionFrameLayout Layout { get; } = layout;

        public Expression Expression { get; } = expression;

        public WideExpressionKind Kind { get; } = kind;

        public bool Matches(FunctionFrameLayout candidateLayout, Expression candidateExpression)
            => ReferenceEquals(Layout, candidateLayout) &&
               ReferenceEquals(Expression, candidateExpression);

        public bool CanRun(InterpreterFrame frame)
        {
            if (Kind == WideExpressionKind.Unsupported)
            {
                return false;
            }

            for (var i = 0; i < requiredSlots.Length; i++)
            {
                if (!frame.IsSlotAssigned(requiredSlots[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
