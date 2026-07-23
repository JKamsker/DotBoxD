using DotBoxD.Services.Transport;

namespace DotBoxD.Transports.Tcp;

/// <summary>Reusable completion source for a TCP owned-frame send that suspended.</summary>
internal sealed class TcpFrameSendOperation :
    PooledFrameSendOperation<TcpFrameSendOperation>
{
    private static readonly Func<TcpFrameSendOperation> Factory =
        static () => new TcpFrameSendOperation();

    private TcpFrameSendState _state;

    internal static int RetainedCountForTests => RetainedOperationCount;

    internal static TcpFrameSendOperation? TryRentOrCreate() =>
        TryRentOrCreateOperation(Factory);

    public ValueTask Start(
        ref TcpFrameSendState state,
        ValueTask pendingOperation)
    {
        _state = state;
        state.Clear();
        return IssuePendingOperation(pendingOperation);
    }

    protected override void ResumePendingOperation(ValueTask pendingOperation)
    {
        Exception? error = null;
        bool completed = false;
        ValueTask nextPendingOperation = default;
        try
        {
            pendingOperation.GetAwaiter().GetResult();
            completed = TcpFrameSendDriver.ResumeAfterPending(
                ref _state,
                out nextPendingOperation);
        }
        catch (Exception caught)
        {
            error = caught;
        }

        if (error is not null)
        {
            Complete(error);
            return;
        }

        if (completed)
        {
            Complete(error: null);
            return;
        }

        RegisterPendingOperation(nextPendingOperation);
    }

    protected override void HandlePendingRegistrationFailure(Exception error) =>
        Complete(error);

    protected override void ClearExternalState() => _state.Clear();

    private void Complete(Exception? error)
    {
        var state = _state;
        _state.Clear();
        var cleanupError = TcpFrameSendDriver.FinishAndClear(ref state);
        error = cleanupError ?? error;

        if (error is null)
        {
            PublishResult();
        }
        else
        {
            PublishException(error);
        }
    }
}
