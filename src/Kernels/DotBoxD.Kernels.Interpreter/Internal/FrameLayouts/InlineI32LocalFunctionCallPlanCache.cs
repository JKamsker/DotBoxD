using System.Runtime.CompilerServices;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal abstract class InlineI32LocalFunctionCallAdmissionState
{
    public static InlineI32LocalFunctionCallAdmissionState Observed { get; } = new ObservedState();

    private sealed class ObservedState : InlineI32LocalFunctionCallAdmissionState
    {
    }
}

/// <summary>
/// Keeps bounded positive and negative helper-shape classifications with a
/// prepared plan. Argument assignment state is still checked on every call.
/// </summary>
internal sealed class InlineI32LocalFunctionCallPlanCache : InlineI32LocalFunctionCallAdmissionState
{
    private const int Capacity = 16;
    private readonly Entry?[] _entries = new Entry?[Capacity];

    public bool TryGet(
        CallExpression call,
        InterpreterEvaluator interpreter,
        out InlineI32LocalFunctionCallPlan plan,
        out SandboxFunction? genericFunction)
    {
        Entry? classified = null;
        var start = RuntimeHelpers.GetHashCode(call) & (Capacity - 1);
        for (var offset = 0; offset < Capacity; offset++)
        {
            var index = (start + offset) & (Capacity - 1);
            var entry = Volatile.Read(ref _entries[index]);
            if (ReferenceEquals(entry?.Call, call))
            {
                return Read(entry, out plan, out genericFunction);
            }

            if (entry is not null)
            {
                continue;
            }

            classified ??= Classify(call, interpreter);
            var raced = Interlocked.CompareExchange(ref _entries[index], classified, null);
            if (raced is null)
            {
                return Read(classified, out plan, out genericFunction);
            }

            if (ReferenceEquals(raced.Call, call))
            {
                return Read(raced, out plan, out genericFunction);
            }
        }

        plan = null!;
        genericFunction = null;
        return false;
    }

    private static Entry Classify(
        CallExpression call,
        InterpreterEvaluator interpreter)
    {
        var eligible = InlineI32LocalFunctionCallEvaluator.TryCreatePlan(
            call,
            interpreter,
            out var plan,
            out var genericFunction);
        return new Entry(call, eligible ? plan : null, genericFunction);
    }

    private static bool Read(
        Entry entry,
        out InlineI32LocalFunctionCallPlan plan,
        out SandboxFunction? genericFunction)
    {
        genericFunction = entry.GenericFunction;
        if (entry.Plan is { } cached)
        {
            plan = cached;
            return true;
        }

        plan = null!;
        return false;
    }

    private sealed record Entry(
        CallExpression Call,
        InlineI32LocalFunctionCallPlan? Plan,
        SandboxFunction? GenericFunction);
}
