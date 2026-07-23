using System.Runtime.CompilerServices;
using DotBoxD.Services.Transport;

namespace DotBoxD.Transports.Tcp;

/// <summary>Reusable completion source for a TCP frame send that suspended.</summary>
internal sealed class TcpFrameSendOperation :
    PooledFrameSendOperation<TcpFrameSendOperation>
{
    private static readonly Func<TcpFrameSendOperation> Factory =
        static () => new TcpFrameSendOperation();

    private static bool s_isAtCapacity;
    private static bool s_requiresPreflight;
    private TcpFrameSendState _state;

    // A stale false only selects the safe transferred-state fallback for that send.
    internal static bool IsAtCapacity => Volatile.Read(ref s_isAtCapacity);
    internal static bool RequiresPreflight => Volatile.Read(ref s_requiresPreflight);
    internal static int RetainedCountForTests => RetainedOperationCount;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool MustUseRawFallback()
    {
        if (!HasAvailableOperation)
        {
            return true;
        }

        // A returned source makes preflight unnecessary until a pending send drains the pool.
        Volatile.Write(ref s_requiresPreflight, false);
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ObserveAcquiredOperation()
    {
        if (!HasAvailableOperation)
        {
            Volatile.Write(ref s_requiresPreflight, true);
        }
    }

    internal static TcpFrameSendOperation? TryRentOrCreate()
    {
        var operation = TryRentOrCreateOperation(Factory);
        if (operation is null)
        {
            Volatile.Write(ref s_isAtCapacity, true);
            Volatile.Write(ref s_requiresPreflight, true);
        }

        return operation;
    }

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
