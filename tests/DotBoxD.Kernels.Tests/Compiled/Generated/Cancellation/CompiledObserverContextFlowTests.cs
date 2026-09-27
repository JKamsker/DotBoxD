using DotBoxD.Hosting.Execution.Compiled;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.Generated;

public sealed class CompiledObserverContextFlowTests
{
    public static IEnumerable<object[]> FlowCases()
    {
        foreach (var state in new[] { "unstarted", "pending", "success", "fault", "canceled" })
        {
            yield return [state, false];
            yield return [state, true];
        }
    }

    [Theory]
    [MemberData(nameof(FlowCases))]
    public async Task Observing_preserves_caller_ambient_values_and_flow_suppression(string state, bool suppressed)
    {
        var value = new object();
        Task<object?> queued;
        var previous = AmbientContextLifetime.Current.Value;
        AmbientContextLifetime.Current.Value = value;
        try
        {
            queued = RegisterAndQueue(state, suppressed);
            Assert.False(ExecutionContext.IsFlowSuppressed());
            Assert.Same(value, AmbientContextLifetime.Current.Value);
        }
        finally
        {
            AmbientContextLifetime.Current.Value = previous;
        }
        Assert.Same(suppressed ? null : value, await queued);
    }

    private static Task<object?> RegisterAndQueue(string state, bool suppressed)
    {
        using var flow = suppressed ? ExecutionContext.SuppressFlow() : default;
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = new Lazy<Task<int>>(() => completion.Task);
        if (state != "unstarted")
        {
            _ = work.Value;
        }
        if (state == "success")
        {
            completion.SetResult(1);
        }
        else if (state == "fault")
        {
            completion.SetException(new InvalidOperationException("expected observed failure"));
        }
        else if (state == "canceled")
        {
            completion.SetCanceled();
        }

        try
        {
            CompiledWorkFailureObserver.Observe(work);
            Assert.Equal(state != "unstarted", work.IsValueCreated);
            Assert.Equal(suppressed, ExecutionContext.IsFlowSuppressed());
            return Task.Run(() => AmbientContextLifetime.Current.Value);
        }
        finally
        {
            completion.TrySetCanceled();
        }
    }
}
