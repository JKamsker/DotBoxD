namespace DotBoxD.Kernels.Interpreter.Internal.Expressions;

internal sealed partial class I32ExpressionPlan
{
    public bool HasOnlyRawVariables()
    {
        if (HasNoVariableOrRawVariables())
        {
            return true;
        }

        if (HasOneOperand())
        {
            return _left!.HasOnlyRawVariables();
        }

        return HasTwoOperands() &&
               _left!.HasOnlyRawVariables() &&
               _right!.HasOnlyRawVariables();
    }

    public int[] GetRequiredRawSlots()
    {
        var slots = new List<int>();
        CollectRequiredRawSlots(slots);
        return slots.ToArray();
    }

    internal void CollectRequiredRawSlots(List<int> slots)
    {
        if (TryCollectDirectRawSlots(slots))
        {
            return;
        }

        if (HasOneOperand())
        {
            _left!.CollectRequiredRawSlots(slots);
            return;
        }

        if (HasTwoOperands())
        {
            _left!.CollectRequiredRawSlots(slots);
            _right!.CollectRequiredRawSlots(slots);
        }
    }

    private bool HasNoVariableOrRawVariables()
        => _kind switch
        {
            ExpressionKind.Literal => true,
            ExpressionKind.RawVariable => true,
            ExpressionKind.RemainderAddRawRawConst => true,
            ExpressionKind.RemainderAddRawConstConst => true,
            ExpressionKind.AddRawMultiplyRawConst => true,
            _ => false
        };

    private bool HasOneOperand()
        => _kind switch
        {
            ExpressionKind.Negate => true,
            ExpressionKind.InlineCall => true,
            ExpressionKind.RemainderByConst => true,
            ExpressionKind.DivideByConst => true,
            _ => false
        };

    private bool HasTwoOperands()
        => _kind switch
        {
            ExpressionKind.Add => true,
            ExpressionKind.Subtract => true,
            ExpressionKind.Multiply => true,
            ExpressionKind.Divide => true,
            ExpressionKind.Remainder => true,
            _ => false
        };

    private bool TryCollectDirectRawSlots(List<int> slots)
    {
        if (_kind is ExpressionKind.RawVariable or ExpressionKind.RemainderAddRawConstConst)
        {
            AddSlot(slots, _value);
            return true;
        }

        if (_kind is ExpressionKind.RemainderAddRawRawConst or ExpressionKind.AddRawMultiplyRawConst)
        {
            AddSlot(slots, _value);
            AddSlot(slots, _value2);
            return true;
        }

        return _kind == ExpressionKind.Literal;
    }

    private static void AddSlot(List<int> slots, int slot)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            if (slots[i] == slot)
            {
                return;
            }
        }

        slots.Add(slot);
    }
}
