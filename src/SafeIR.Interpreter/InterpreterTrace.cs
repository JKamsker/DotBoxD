namespace SafeIR.Interpreter;

using SafeIR;

internal static class InterpreterTrace
{
    public static void Write(
        SandboxContext context,
        SandboxExecutionOptions options,
        string functionId,
        string category,
        string nodeKind)
    {
        if (!options.EnableDebugTrace) {
            return;
        }

        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "DebugTrace",
            DateTimeOffset.UtcNow,
            true,
            Message: $"function={functionId} node={category}:{nodeKind} fuelRemaining={RemainingFuel(context)}"));
    }

    private static long RemainingFuel(SandboxContext context)
        => context.Budget.Limits.MaxFuel - context.Budget.FuelUsed;
}
