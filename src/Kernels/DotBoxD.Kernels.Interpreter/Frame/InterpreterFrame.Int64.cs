using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Frame;

// Unboxed i64 slot access, mirroring the i32/f64 raw-slot accessors. Used by I64ExpressionPlan / I64ForLoopRunner.
internal sealed partial class InterpreterFrame
{
    public bool IsI64Slot(int slot) => _layout.IsI64Slot(slot);

    public long ReadRawInt64Slot(int slot) => _i64Slots[slot];

    public void WriteRawInt64Slot(int slot, long value)
    {
        _i64Slots[slot] = value;
        RawSlotAssignmentState.MarkAssigned(_assigned, slot);
    }

    public bool TryReadInt64(string name, out long value)
    {
        var slot = _layout.GetSlot(name);
        if (_layout.IsI64Slot(slot))
        {
            value = RawSlotAssignmentState.IsAssigned(_assigned, slot) ? _i64Slots[slot] : 0;
            return RawSlotAssignmentState.IsAssigned(_assigned, slot);
        }

        if (TryGetBoxedValue<I64Value>(slot, out var i64))
        {
            value = i64.Value;
            return true;
        }

        value = 0;
        return false;
    }
}
