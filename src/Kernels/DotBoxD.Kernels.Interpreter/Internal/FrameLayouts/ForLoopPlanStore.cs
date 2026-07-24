using DotBoxD.Kernels.Interpreter.Frame;

namespace DotBoxD.Kernels.Interpreter.Internal;

/// <summary>
/// Holds a frame layout's lazily initialized for-range plan admission and cache state.
/// As an inline mutable value, the first observed statement needs no extra owner allocation.
/// </summary>
internal struct ForLoopPlanStore
{
    private TwoHitPlanAdmission<ForRangeStatement> _admission;
    private ForLoopPlanCache? _plans;

    public bool TryGetI32(
        ForRangeStatement statement,
        InterpreterFrame frame,
        int loopSlot,
        out I32ForLoopPlan plan)
        => TryGetI32(statement, frame, loopSlot, loopSlot, out plan);

    public bool TryGetI32(
        ForRangeStatement statement,
        InterpreterFrame frame,
        int loopSlot,
        int slotWrittenBeforeEvaluation,
        out I32ForLoopPlan plan)
    {
        var cache = Volatile.Read(ref _plans);
        if (cache is null)
        {
            plan = null!;
            return false;
        }

        return cache.TryGetI32(
            statement,
            frame,
            loopSlot,
            slotWrittenBeforeEvaluation,
            out plan);
    }

    public bool TryGetI64(
        ForRangeStatement statement,
        InterpreterFrame frame,
        out I64ForLoopPlan plan)
    {
        var cache = Volatile.Read(ref _plans);
        if (cache is null)
        {
            plan = null!;
            return false;
        }

        return cache.TryGetI64(statement, frame, out plan);
    }

    public bool TryGetF64(
        ForRangeStatement statement,
        InterpreterFrame frame,
        out F64ForLoopPlan plan)
    {
        var cache = Volatile.Read(ref _plans);
        if (cache is null)
        {
            plan = null!;
            return false;
        }

        return cache.TryGetF64(statement, frame, out plan);
    }

    public bool ShouldCache(ForRangeStatement statement)
    {
        var cache = Volatile.Read(ref _plans);
        if (cache?.Contains(statement) == true)
        {
            return false;
        }

        return _admission.ShouldCache(statement);
    }

    public void Cache(IForLoopPlan plan)
    {
        var cache = Volatile.Read(ref _plans);
        if (cache is null)
        {
            var created = new ForLoopPlanCache();
            cache = Interlocked.CompareExchange(ref _plans, created, null) ?? created;
        }

        cache.Store(plan);
        _admission.Forget(plan.Statement);
    }
}
