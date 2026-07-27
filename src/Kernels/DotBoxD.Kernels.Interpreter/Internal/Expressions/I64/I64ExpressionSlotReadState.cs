using DotBoxD.Kernels.Interpreter.Frame;

namespace DotBoxD.Kernels.Interpreter.Internal.Expressions;

// Carries the frame assignment state and the targets established by earlier statements while an i64 loop body
// is planned. Keeping this as value state lets recursive expression planning query slot readability without a
// captured predicate or delegate allocation.
internal readonly struct I64ExpressionSlotReadState
{
    private readonly InterpreterFrame _frame;
    private readonly HashSet<int>? _earlierAssignmentTargets;

    public I64ExpressionSlotReadState(InterpreterFrame frame, HashSet<int>? earlierAssignmentTargets = null)
    {
        _frame = frame;
        _earlierAssignmentTargets = earlierAssignmentTargets;
    }

    public bool TryGetReadableI64Slot(string name, out int slot)
    {
        slot = _frame.GetSlot(name);
        return _frame.IsI64Slot(slot) &&
               (_frame.IsSlotAssigned(slot) || _earlierAssignmentTargets?.Contains(slot) == true);
    }
}
