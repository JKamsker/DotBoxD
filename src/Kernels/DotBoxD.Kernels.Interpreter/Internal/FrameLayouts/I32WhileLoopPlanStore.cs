using DotBoxD.Kernels.Interpreter.Frame;

namespace DotBoxD.Kernels.Interpreter.Internal;

/// <summary>
/// Admits a reusable i32 while-loop plan only after the same statement has
/// produced a safe plan twice, avoiding cache objects for one-shot loops.
/// </summary>
internal struct I32WhileLoopPlanStore
{
    private TwoHitPlanAdmission<WhileStatement> _admission;
    private I32WhileLoopPlanCache? _plans;

    public bool TryGet(
        WhileStatement statement,
        InterpreterFrame frame,
        out I32WhileLoopPlan plan)
    {
        var cache = Volatile.Read(ref _plans);
        if (cache is null)
        {
            plan = null!;
            return false;
        }

        return cache.TryGet(statement, frame, out plan);
    }

    public bool ShouldCache(WhileStatement statement)
    {
        var cache = Volatile.Read(ref _plans);
        if (cache?.Contains(statement) == true)
        {
            return false;
        }

        return _admission.ShouldCache(statement);
    }

    public void Cache(I32WhileLoopPlan plan)
    {
        var cache = Volatile.Read(ref _plans);
        if (cache is null)
        {
            var created = new I32WhileLoopPlanCache();
            cache = Interlocked.CompareExchange(ref _plans, created, null) ?? created;
        }

        cache.Store(plan);
        _admission.Forget(plan.Statement);
    }
}
