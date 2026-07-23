using System.Runtime.CompilerServices;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Transport;

/// <summary>Reusable completion source for a StreamConnection read that genuinely suspended.</summary>
internal sealed class StreamFrameReceiveOperation :
    PooledFrameReceiveOperation<StreamFrameReceiveOperation>
{
    private static readonly ContextCallback ResumeInContext =
        static state => ((StreamFrameReceiveOperation)state!).ResumeCore();

    private readonly Action _resume;
    private StreamConnection? _connection;
    private ExecutionContext? _executionContext;
    private FrameReceiveOperationState _state;
    private ValueTask<int> _pendingRead;
    private int _leaseGeneration;

    internal StreamFrameReceiveOperation() => _resume = Resume;

    public static ValueTask<RpcFrame> Start(StreamConnection connection, CancellationToken ct)
    {
        if (StreamFrameReceiveOperationPopulation.RequiresPreflight &&
            StreamFrameReceiveOperationPopulation.MustUseFallback())
        {
            return StreamFrameReceiveFallback.StartFromBeginning(connection, ct);
        }

        var state = new FrameReceiveOperationState();
        RpcFrame frame;
        ValueTask<int> pendingRead;
        bool completed;
        try
        {
            StreamFrameReceiveDriver.Initialize(connection, ct, ref state);
            completed = StreamFrameReceiveDriver.TryAdvance(
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

        StreamFrameReceiveOperation? operation;
        try
        {
            operation = TryRentOperation();
            if (operation is null)
            {
                operation = StreamFrameReceiveOperationPopulation.CreateOrRentRaced();
            }
        }
        catch (Exception error)
        {
            return FailSynchronously(connection, ref state, error);
        }

        if (operation is null)
        {
            try
            {
                var fallback = StreamFrameReceiveFallback.Start(connection, state, pendingRead);
                state = default;
                return fallback;
            }
            catch (Exception error)
            {
                return FailSynchronously(connection, ref state, error);
            }
        }

        if (StreamFrameReceiveOperationPopulation.IsAtCapacity)
        {
            StreamFrameReceiveOperationPopulation.ObserveAcquiredOperation();
        }

        return StartPending(operation, connection, ref state, pendingRead);
    }

    internal static bool HasAvailableOperationForPopulation => HasAvailableOperation;

    internal static StreamFrameReceiveOperation? TryRentOperationForPopulation() =>
        TryRentOperation();

    private static ValueTask<RpcFrame> StartPending(
        StreamFrameReceiveOperation operation,
        StreamConnection connection,
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
        StreamConnection connection,
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
        StreamConnection connection,
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
            "A pooled receive continuation ran without an active connection.");
        var pendingRead = _pendingRead;
        _pendingRead = default;

        RpcFrame frame = default;
        ValueTask<int> nextPendingRead = default;
        Exception? error = null;
        bool completed = false;
        try
        {
            completed = StreamFrameReceiveDriver.Resume(
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
            "A pooled receive completed without an active connection.");
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
