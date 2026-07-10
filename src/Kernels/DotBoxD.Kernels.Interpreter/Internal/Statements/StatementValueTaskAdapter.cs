using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal static class StatementValueTaskAdapter
{
    public static ValueTask<SandboxValue?> AsNullable(ValueTask<SandboxValue> task)
        => task.IsCompletedSuccessfully
            ? new ValueTask<SandboxValue?>(task.Result)
            : AwaitNullable(task);

    public static ValueTask<SandboxValue?> DiscardResult(ValueTask<SandboxValue> task)
    {
        if (task.IsCompletedSuccessfully)
        {
            _ = task.Result;
            return default;
        }

        return AwaitDiscard(task);
    }

    private static async ValueTask<SandboxValue?> AwaitNullable(ValueTask<SandboxValue> task)
        => await task.ConfigureAwait(false);

    private static async ValueTask<SandboxValue?> AwaitDiscard(ValueTask<SandboxValue> task)
    {
        _ = await task.ConfigureAwait(false);
        return null;
    }
}
