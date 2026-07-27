namespace DotBoxD.Services.Transport;

/// <summary>Reusable completion source for a StreamConnection send that genuinely suspended.</summary>
internal sealed class StreamFrameSendOperation :
    PooledFrameSendOperation<StreamFrameSendOperation>
{
    private static readonly Func<StreamFrameSendOperation> Factory =
        static () => new StreamFrameSendOperation();

    private StreamFrameSendState _state;

    private StreamFrameSendOperation()
    {
    }

    internal static int RetainedCount => RetainedOperationCount;

    internal static StreamFrameSendOperation? TryRentOrCreate() =>
        TryRentOrCreateOperation(Factory);

    public ValueTask Start(
        ref StreamFrameSendState state,
        ValueTask pendingOperation)
    {
        _state = state;
        state = default;
        return IssuePendingOperation(pendingOperation);
    }

    protected override void ResumePendingOperation(ValueTask pendingOperation)
    {
        ValueTask nextPendingOperation = default;
        Exception? error = null;
        bool completed = false;
        try
        {
            completed = StreamFrameSendDriver.Resume(
                ref _state,
                pendingOperation,
                out nextPendingOperation);
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
            CompleteResult();
            return;
        }

        RegisterPendingOperation(nextPendingOperation);
    }

    protected override void HandlePendingRegistrationFailure(Exception error) =>
        CompleteException(error);

    protected override void ClearExternalState() => _state = default;

    private void CompleteResult()
    {
        var cleanupError = FinishAndClear();
        if (cleanupError is null)
        {
            PublishResult();
        }
        else
        {
            PublishException(cleanupError);
        }
    }

    private void CompleteException(Exception error)
    {
        var cleanupError = FinishAndClear();
        PublishException(cleanupError ?? error);
    }

    private Exception? FinishAndClear()
    {
        var state = _state;
        _state = default;
        return StreamFrameSendCleanup.Finish(ref state);
    }
}
