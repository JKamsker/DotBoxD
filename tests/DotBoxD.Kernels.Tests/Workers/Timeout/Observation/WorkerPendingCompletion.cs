using System.Threading.Tasks.Sources;

namespace DotBoxD.Kernels.Tests.Workers;

internal sealed class WorkerPendingCompletion(
    SandboxExecutionResult result, CancellationToken cancellationToken, bool customSource)
    : IValueTaskSource<SandboxExecutionResult>
{
    private readonly TaskCompletionSource<SandboxExecutionResult> _task =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ManualResetValueTaskSourceCore<SandboxExecutionResult> _source = new()
    {
        RunContinuationsAsynchronously = true,
    };

    internal SandboxExecutionResult Result => result;
    internal CancellationToken CancellationToken => cancellationToken;
    internal ConsumptionCounter Counter { get; } = new();
    internal bool IsCompleted => customSource
        ? _source.GetStatus(_source.Version) != ValueTaskSourceStatus.Pending
        : _task.Task.IsCompleted;

    internal ValueTask<SandboxExecutionResult> AsValueTask()
        => customSource ? new(this, _source.Version) : new(_task.Task);

    internal void Succeed()
    {
        if (customSource)
        {
            _source.SetResult(result);
        }
        else
        {
            _task.SetResult(result);
        }
    }

    internal void Fail(Exception error)
    {
        if (customSource)
        {
            _source.SetException(error);
        }
        else if (error is OperationCanceledException cancellation)
        {
            _task.SetCanceled(cancellation.CancellationToken);
        }
        else
        {
            _task.SetException(error);
        }
    }

    public SandboxExecutionResult GetResult(short token)
    {
        Interlocked.Increment(ref Counter.Calls);
        return _source.GetResult(token);
    }

    public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _source.OnCompleted(continuation, state, token, flags);

    internal sealed class ConsumptionCounter
    {
        internal int Calls;
    }
}
