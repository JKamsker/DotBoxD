using System.Collections.Concurrent;
using DotBoxD.Kernels.Interpreter.Frame;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal sealed class FunctionFrameLayoutCache
{
    private readonly ConcurrentDictionary<string, Lazy<FunctionFrameLayout>> _layouts = new(StringComparer.Ordinal);
    private WideExpressionAdmissionState? _wideExpressionState;

    public FunctionFrameLayout Get(SandboxFunction function, ExecutionPlan plan)
        => _layouts.GetOrAdd(
            function.Id,
            static (_, state) => new Lazy<FunctionFrameLayout>(
                () => FunctionFrameLayout.Build(
                    state.Function,
                    state.Plan.FunctionAnalysis,
                    state.Plan.Bindings),
                LazyThreadSafetyMode.ExecutionAndPublication),
            (Function: function, Plan: plan)).Value;

    public bool TryGetWideExpressionKind(
        Expression expression,
        InterpreterFrame frame,
        out WideExpressionKind kind)
    {
        var state = Volatile.Read(ref _wideExpressionState);
        if (state is WideExpressionEligibilityCache cache)
        {
            return cache.TryGetKind(expression, frame, out kind);
        }

        if (state is null)
        {
            state = Interlocked.CompareExchange(
                ref _wideExpressionState,
                WideExpressionAdmissionState.Observed,
                null);
            if (state is null)
            {
                kind = WideExpressionKind.Unsupported;
                return false;
            }
        }

        if (state is WideExpressionEligibilityCache published)
        {
            return published.TryGetKind(expression, frame, out kind);
        }

        var created = new WideExpressionEligibilityCache();
        var raced = Interlocked.CompareExchange(
            ref _wideExpressionState,
            created,
            WideExpressionAdmissionState.Observed);
        cache = ReferenceEquals(raced, WideExpressionAdmissionState.Observed)
            ? created
            : (WideExpressionEligibilityCache)raced!;
        return cache.TryGetKind(expression, frame, out kind);
    }
}
