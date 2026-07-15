using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Frame;

internal static class InterpreterFrameBuilder
{
    public static InterpreterFrame Create(
        FunctionFrameLayout layout,
        SandboxFunction function,
        IReadOnlyList<SandboxValue> args)
    {
        var slots = layout.HasBoxedSlots ? new SandboxValue?[layout.SlotCount] : Array.Empty<SandboxValue?>();
        var i32Slots = layout.HasI32Slots ? new int[layout.SlotCount] : Array.Empty<int>();
        var i64Slots = layout.HasI64Slots ? new long[layout.SlotCount] : Array.Empty<long>();
        var f64Slots = layout.HasF64Slots ? new double[layout.SlotCount] : Array.Empty<double>();
        var assigned = layout.HasRawSlots ? new bool[layout.SlotCount] : Array.Empty<bool>();

        // Parameters occupy the leading slots in declaration order (see
        // FunctionFrameLayout.Build), so positional arguments map directly.
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            AssignArgumentSlot(layout, args[i], i, slots, i32Slots, i64Slots, f64Slots, assigned);
        }

        return new InterpreterFrame(layout, slots, i32Slots, i64Slots, f64Slots, assigned);
    }

    private static void AssignArgumentSlot(
        FunctionFrameLayout layout,
        SandboxValue argument,
        int slot,
        SandboxValue?[] slots,
        int[] i32Slots,
        long[] i64Slots,
        double[] f64Slots,
        bool[] assigned)
    {
        if (layout.IsI32Slot(slot))
        {
            i32Slots[slot] = ((I32Value)argument).Value;
        }
        else if (layout.IsF64Slot(slot))
        {
            f64Slots[slot] = ((F64Value)argument).Value;
        }
        else if (layout.IsI64Slot(slot))
        {
            i64Slots[slot] = ((I64Value)argument).Value;
        }
        else
        {
            slots[slot] = argument;
        }

        if (layout.HasRawSlots)
        {
            assigned[slot] = true;
        }
    }
}
