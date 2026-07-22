using System.Threading.Tasks.Sources;

namespace DotBoxD.Services.Transport;

/// <summary>Owns the reusable ValueTask source lifecycle shared by framed transports.</summary>
internal abstract class PooledFrameReceiveOperation<TOperation> : IValueTaskSource<RpcFrame>
    where TOperation : PooledFrameReceiveOperation<TOperation>
{
    private static readonly BoundedFrameReceiveOperationPool<TOperation> Pool = new();

    private ManualResetValueTaskSourceCore<RpcFrame> _source;
    private PooledFrameReceiveOperationLifecycle _lifecycle;

    protected static TOperation? TryRentOperation() => Pool.Rent();

    protected ValueTask<RpcFrame> IssueValueTask()
    {
        _lifecycle.Initialize();
        return new ValueTask<RpcFrame>(this, _source.Version);
    }

    protected void PublishResult(RpcFrame frame)
    {
        try
        {
            _source.SetResult(frame);
        }
        finally
        {
            if (_lifecycle.FinishProducer())
            {
                Recycle();
            }
        }
    }

    protected void PublishException(Exception error)
    {
        try
        {
            _source.SetException(error);
        }
        finally
        {
            if (_lifecycle.FinishProducer())
            {
                Recycle();
            }
        }
    }

    public RpcFrame GetResult(short token)
    {
        if (!_lifecycle.TryBeginReading())
        {
            throw new InvalidOperationException(
                "The receive operation result is already being consumed or was consumed.");
        }

        if (token != _source.Version)
        {
            _lifecycle.RollBackReading();
            throw new InvalidOperationException("The receive operation token is no longer valid.");
        }

        ValueTaskSourceStatus status;
        try
        {
            status = _source.GetStatus(token);
        }
        catch
        {
            _lifecycle.RollBackReading();
            throw;
        }

        if (status == ValueTaskSourceStatus.Pending)
        {
            _lifecycle.RollBackReading();
            throw new InvalidOperationException("The receive operation has not completed.");
        }

        try
        {
            return _source.GetResult(token);
        }
        finally
        {
            if (_lifecycle.FinishReading())
            {
                Recycle();
            }
        }
    }

    public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);

    public void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags) =>
        _source.OnCompleted(continuation, state, token, flags);

    protected abstract void ClearForRecycle();

    private void Recycle()
    {
        ClearForRecycle();
        _source.Reset();
        Pool.Return((TOperation)this);
    }
}
