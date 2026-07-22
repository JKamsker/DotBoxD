using DotBoxD.Kernels.Interpreter.Frame;

namespace DotBoxD.Kernels.Interpreter.Internal;

/// <summary>
/// Owns the immutable loop plans learned by one function frame layout. Admission
/// state stays separate for each supported loop shape. The mutable value lives
/// inline in its layout so observing a one-shot loop needs no owner allocation.
/// </summary>
internal struct FunctionLoopPlans
{
    private I32LoopPlanStore _i32ForRangePlans;
    private I32BranchedLoopPlanStore _i32BranchedForRangePlans;
    private I32WhileLoopPlanStore _i32WhilePlans;

    public bool TryGetI32ForRangePlan(
        ForRangeStatement statement,
        InterpreterFrame frame,
        int loopSlot,
        out I32ForLoopPlan plan)
        => _i32ForRangePlans.TryGet(statement, frame, loopSlot, out plan);

    public bool ShouldCacheI32ForRangePlan(ForRangeStatement statement)
        => _i32ForRangePlans.ShouldCache(statement);

    public void CacheI32ForRangePlan(I32ForLoopPlan plan)
        => _i32ForRangePlans.Cache(plan);

    public bool TryGetI32BranchedForRangePlan(
        ForRangeStatement statement,
        InterpreterFrame frame,
        out I32BranchedLoopPlan plan)
        => _i32BranchedForRangePlans.TryGet(statement, frame, out plan);

    public bool ShouldCacheI32BranchedForRangePlan(ForRangeStatement statement)
        => _i32BranchedForRangePlans.ShouldCache(statement);

    public void CacheI32BranchedForRangePlan(I32BranchedLoopPlan plan)
        => _i32BranchedForRangePlans.Cache(plan);

    public bool TryGetI32WhilePlan(
        WhileStatement statement,
        InterpreterFrame frame,
        out I32WhileLoopPlan plan)
        => _i32WhilePlans.TryGet(statement, frame, out plan);

    public bool ShouldCacheI32WhilePlan(WhileStatement statement)
        => _i32WhilePlans.ShouldCache(statement);

    public void CacheI32WhilePlan(I32WhileLoopPlan plan)
        => _i32WhilePlans.Cache(plan);
}
