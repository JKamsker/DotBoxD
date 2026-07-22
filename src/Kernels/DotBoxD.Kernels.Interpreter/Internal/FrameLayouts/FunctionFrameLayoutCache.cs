using System.Collections.Concurrent;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal sealed class FunctionFrameLayoutCache
{
    private readonly ExecutionPlan _plan;
    private readonly ConcurrentDictionary<string, Lazy<FunctionFrameLayout>> _layouts = new(StringComparer.Ordinal);

    public FunctionFrameLayoutCache(ExecutionPlan plan)
    {
        _plan = plan;
    }

    public FunctionFrameLayout Get(SandboxFunction function)
        => _layouts.GetOrAdd(
            function.Id,
            static (_, state) => new Lazy<FunctionFrameLayout>(
                () => FunctionFrameLayout.Build(
                    state.Function,
                    state.Cache._plan.FunctionAnalysis,
                    state.Cache._plan.Bindings),
                LazyThreadSafetyMode.ExecutionAndPublication),
            (Cache: this, Function: function)).Value;
}
