namespace DotBoxD.Kernels.Interpreter.Frame;

internal static class RawSlotAssignmentState
{
    public static bool IsAssigned(bool[] assignedSlots, int slot)
        => assignedSlots.Length == 0 || assignedSlots[slot];

    public static void MarkAssigned(bool[] assignedSlots, int slot)
    {
        if (assignedSlots.Length != 0)
        {
            assignedSlots[slot] = true;
        }
    }
}
