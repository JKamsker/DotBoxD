using System.Runtime.CompilerServices;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;

namespace DotBoxD.Transports.Tcp;

/// <summary>Reusable completion source for a TCP frame read that genuinely suspended.</summary>
internal sealed class TcpFrameReceiveOperation :
    PooledFrameReceiveOperation<TcpFrameReceiveOperation>
{
    private static readonly ContextCallback ResumeInContext =
        static state => ((TcpFrameReceiveOperation)state!).ResumeCore();

    private readonly Action _resume;
    private TcpConnection? _connection;
    private ExecutionContext? _executionContext;
    private TcpFrameReceiveOperationCache? _returnCache;
    private FrameReceiveOperationState _state;
    private ValueTask<int> _pendingRead;
    private int _leaseGeneration;

    internal TcpFrameReceiveOperation() => _resume = Resume;

    public static ValueTask<RpcFrame> Start(TcpConnection connection, CancellationToken ct)
    {
        if (TcpFrameReceiveOperationAcquisition.MustUseFallback(connection))
        {
            return TcpFrameReceiveFallback.StartFromBeginning(connection, ct);
        }

        var state = new FrameReceiveOperationState();
        RpcFrame frame;
        ValueTask<int> pendingRead;
        bool completed;
        try
        {
            TcpFrameReceiveDriver.Initialize(connection, ct, ref state);
            completed = TcpFrameReceiveDriver.TryAdvance(
                connection,
                ref state,
                out frame,
                out pendingRead);
        }
        catch (Exception error)
        {
            return FailSynchronously(connection, ref state, error);
        }

        if (completed)
        {
            return CompleteSynchronously(connection, ref state, frame);
        }

        TcpFrameReceiveOperation? operation;
        try
        {
            operation = TcpFrameReceiveOperationAcquisition.Rent(connection);
        }
        catch (Exception error)
        {
            return FailSynchronously(connection, ref state, error);
        }

        if (operation is null)
        {
            try
            {
                var fallback = TcpFrameReceiveFallback.Start(connection, state, pendingRead);
                state = default;
                return fallback;
            }
            catch (Exception error)
            {
                return FailSynchronously(connection, ref state, error);
            }
        }

        return StartPending(operation, connection, ref state, pendingRead);
    }

    internal static bool HasAvailableOperationForPopulation => HasAvailableOperation;

    internal static TcpFrameReceiveOperation? TryRentOperationForPopulation() =>
        TryRentOperation();

    internal void ReturnTo(TcpFrameReceiveOperationCache cache) => _returnCache = cache;

    private static ValueTask<RpcFrame> StartPending(
        TcpFrameReceiveOperation operation,
        TcpConnection connection,
        ref FrameReceiveOperationState state,
        ValueTask<int> pendingRead)
    {
        operation._connection = connection;
        operation._state = state;
        operation._pendingRead = pendingRead;
        var generation = operation.NextLeaseGeneration();
        state = default;

        var result = operation.IssueValueTask();
        operation.RegisterPendingRead(generation);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ValueTask<RpcFrame> CompleteSynchronously(
        TcpConnection connection,
        ref FrameReceiveOperationState state,
        RpcFrame frame)
    {
        try
        {
            connection.FinishFrameReceive(ref state);
        }
        catch (Exception error)
        {
            frame.Dispose();
            return FrameReceiveFailure.Create(error, state.CallerToken);
        }

        return new ValueTask<RpcFrame>(frame);
    }

    private static ValueTask<RpcFrame> FailSynchronously(
        TcpConnection connection,
        ref FrameReceiveOperationState state,
        Exception error)
    {
        var callerToken = state.CallerToken;
        try
        {
            connection.FinishFrameReceive(ref state);
        }
        catch (Exception cleanupError)
        {
            error = cleanupError;
        }

        return FrameReceiveFailure.Create(error, callerToken);
    }

    private void Resume()
    {
        var executionContext = _executionContext;
        _executionContext = null;
        if (executionContext is null)
        {
            ResumeCore();
            return;
        }

        ExecutionContext.Run(executionContext, ResumeInContext, this);
    }

    private void ResumeCore()
    {
        var connection = _connection ?? throw new InvalidOperationException(
            "A pooled TCP receive continuation ran without an active connection.");
        var pendingRead = _pendingRead;
        _pendingRead = default;

        RpcFrame frame = default;
        ValueTask<int> nextPendingRead = default;
        Exception? error = null;
        bool completed = false;
        try
        {
            completed = TcpFrameReceiveDriver.Resume(
                connection,
                ref _state,
                pendingRead,
                out frame,
                out nextPendingRead);
            if (!completed)
            {
                _pendingRead = nextPendingRead;
            }
        }
        catch (Exception caught)
        {
            error = caught;
        }

        if (error is not null)
        {
            CompleteException(error);
            return;
        }

        if (completed)
        {
            CompleteResult(frame);
            return;
        }

        RegisterPendingRead(Volatile.Read(ref _leaseGeneration));
    }

    private void RegisterPendingRead(int generation)
    {
        try
        {
            _executionContext = ExecutionContext.Capture();
            _pendingRead.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(_resume);
        }
        catch (Exception error)
        {
            if (generation != Volatile.Read(ref _leaseGeneration) || _connection is null)
            {
                throw;
            }

            if (error is OperationCanceledException &&
                StreamFrameReadOperations.IsTimeoutCancellation(_connection.FrameReceiveTimeout))
            {
                error = FrameReadTimeoutSource.CreateTimeoutException(
                    _connection.FrameReadIdleTimeout);
            }

            CompleteException(error);
        }
    }

    private void CompleteResult(RpcFrame frame)
    {
        var cleanupError = FinishAndClear();
        if (cleanupError is not null)
        {
            frame.Dispose();
            PublishException(cleanupError);
            return;
        }

        PublishResult(frame);
    }

    private void CompleteException(Exception error)
    {
        var cleanupError = FinishAndClear();
        PublishException(cleanupError ?? error);
    }

    private Exception? FinishAndClear()
    {
        var connection = _connection ?? throw new InvalidOperationException(
            "A pooled TCP receive completed without an active connection.");
        Exception? cleanupError = null;
        try
        {
            connection.FinishFrameReceive(ref _state);
        }
        catch (Exception error)
        {
            cleanupError = error;
        }

        ClearExternalState();
        return cleanupError;
    }

    protected override void ClearForRecycle() => ClearExternalState();

    protected override bool TryReturnOperation(TcpFrameReceiveOperation operation)
    {
        var cache = _returnCache;
        _returnCache = null;
        if (cache is null)
        {
            return false;
        }

        cache.Return(operation);
        return true;
    }

    private void ClearExternalState()
    {
        _connection = null;
        _executionContext = null;
        _state = default;
        _pendingRead = default;
    }

    private int NextLeaseGeneration()
    {
        var generation = unchecked(_leaseGeneration + 1);
        if (generation == 0)
        {
            generation = 1;
        }

        Volatile.Write(ref _leaseGeneration, generation);
        return generation;
    }
}
