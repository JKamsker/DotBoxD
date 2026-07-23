using DotBoxD.Kernels.Interpreter.Frame;

namespace DotBoxD.Kernels.Interpreter.Internal;

/// <summary>
/// Holds a frame layout's lazily initialized i32 loop-plan admission and cache state.
/// As an inline mutable value, the first observed statement needs no extra owner allocation.
/// </summary>
internal struct I32LoopPlanStore
{
    private TwoHitPlanAdmission<ForRangeStatement> _admission;
    private I32ForLoopPlanCache? _plans;

    public bool TryGet(
        ForRangeStatement statement,
        InterpreterFrame frame,
        int loopSlot,
        out I32ForLoopPlan plan)
        => TryGet(statement, frame, loopSlot, loopSlot, out plan);

    public bool TryGet(
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

        return cache.TryGet(
            statement,
            frame,
            loopSlot,
            slotWrittenBeforeEvaluation,
            out plan);
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

    public void Cache(I32ForLoopPlan plan)
    {
        var cache = Volatile.Read(ref _plans);
        if (cache is null)
        {
            var created = new I32ForLoopPlanCache();
            cache = Interlocked.CompareExchange(ref _plans, created, null) ?? created;
        }

        cache.Store(plan);
        _admission.Forget(plan.Statement);
    }
}
