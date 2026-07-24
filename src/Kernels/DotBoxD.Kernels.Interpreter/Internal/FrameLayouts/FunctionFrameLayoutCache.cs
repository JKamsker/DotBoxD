using System.Collections.Concurrent;
using DotBoxD.Kernels.Interpreter.Frame;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal sealed class FunctionFrameLayoutCache
{
    private readonly ConcurrentDictionary<string, Lazy<FunctionFrameLayout>> _layouts = new(StringComparer.Ordinal);
    private InlineI32LocalFunctionCallAdmissionState? _inlineI32LocalFunctionCallState;
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

    public bool TryGetInlineI32LocalFunctionCallPlan(
        CallExpression call,
        InterpreterEvaluator interpreter,
        out InlineI32LocalFunctionCallPlan plan,
        out SandboxFunction? genericFunction)
    {
        var state = Volatile.Read(ref _inlineI32LocalFunctionCallState);
        if (state is InlineI32LocalFunctionCallPlanCache cache)
        {
            return cache.TryGet(call, interpreter, out plan, out genericFunction);
        }

        if (state is null)
        {
            state = Interlocked.CompareExchange(
                ref _inlineI32LocalFunctionCallState,
                InlineI32LocalFunctionCallAdmissionState.Observed,
                null);
            if (state is null)
            {
                return InlineI32LocalFunctionCallEvaluator.TryCreatePlan(
                    call,
                    interpreter,
                    out plan,
                    out genericFunction);
            }
        }

        if (state is InlineI32LocalFunctionCallPlanCache published)
        {
            return published.TryGet(call, interpreter, out plan, out genericFunction);
        }

        var created = new InlineI32LocalFunctionCallPlanCache();
        var raced = Interlocked.CompareExchange(
            ref _inlineI32LocalFunctionCallState,
            created,
            InlineI32LocalFunctionCallAdmissionState.Observed);
        cache = ReferenceEquals(raced, InlineI32LocalFunctionCallAdmissionState.Observed)
            ? created
            : (InlineI32LocalFunctionCallPlanCache)raced!;
        return cache.TryGet(call, interpreter, out plan, out genericFunction);
    }

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
