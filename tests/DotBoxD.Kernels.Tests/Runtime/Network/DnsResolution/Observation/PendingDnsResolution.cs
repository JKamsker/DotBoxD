using System.Net;
using System.Threading.Tasks.Sources;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

internal sealed class PendingDnsResolution(bool customSource) : IValueTaskSource<IReadOnlyList<IPAddress>>
{
    private readonly IReadOnlyList<IPAddress> _addresses = [IPAddress.Parse("93.184.216.34")];
    private readonly TaskCompletionSource<IReadOnlyList<IPAddress>> _task =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ManualResetValueTaskSourceCore<IReadOnlyList<IPAddress>> _source = new()
    {
        RunContinuationsAsynchronously = true,
    };

    internal ConsumptionCounter Counter { get; } = new();
    internal CancellationToken CancellationToken { get; private set; }
    internal bool IsCompleted => customSource
        ? _source.GetStatus(_source.Version) != ValueTaskSourceStatus.Pending
        : _task.Task.IsCompleted;

    internal ValueTask<IReadOnlyList<IPAddress>> Resolve(string host, CancellationToken cancellationToken)
    {
        Assert.Equal("api.example.com", host);
        Counter.ResolveCalls++;
        CancellationToken = cancellationToken;
        return customSource ? new(this, _source.Version) : new(_task.Task);
    }

    internal void Succeed()
    {
        if (customSource)
        {
            _source.SetResult(_addresses);
        }
        else
        {
            _task.SetResult(_addresses);
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

    public IReadOnlyList<IPAddress> GetResult(short token)
    {
        Interlocked.Increment(ref Counter.ResultCalls);
        return _source.GetResult(token);
    }

    public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _source.OnCompleted(continuation, state, token, flags);

    internal sealed class ConsumptionCounter
    {
        internal int ResolveCalls;
        internal int ResultCalls;
    }
}
