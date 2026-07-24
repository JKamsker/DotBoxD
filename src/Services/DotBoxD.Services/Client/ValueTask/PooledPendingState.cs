namespace DotBoxD.Services.Client;

internal enum PooledCompletionKind
{
    Normal,
    Caller,
    Timeout
}

internal enum PooledCompletionStart
{
    Rejected,
    TwoPhase,
    DirectAfterSource
}

internal static class PooledPendingStates
{
    internal const int OwnerMask = 0x07;
    internal const int OwnerSetup = 0x00;
    internal const int OwnerWrapper = 0x01;
    internal const int OwnerDirect = 0x02;
    internal const int OwnerDirectReleasing = 0x03;
    internal const int OwnerReleased = 0x04;

    internal const int ConsumerMask = 0x18;
    internal const int ConsumerUnissued = 0x00;
    internal const int ConsumerIssued = 0x08;
    internal const int ConsumerConsumed = 0x10;
    internal const int ConsumerAbandoned = 0x18;

    internal const int CompletionMask = 0xE0;
    internal const int CompletionActive = 0x00;
    internal const int CompletionCompletingNormal = 0x20;
    internal const int CompletionCompletingCaller = 0x40;
    internal const int CompletionCompletingTimeout = 0x60;
    internal const int CompletionCompletedNormal = 0x80;
    internal const int CompletionCompletedCaller = 0xA0;
    internal const int CompletionCompletedTimeout = 0xC0;

    internal const int ReturnClaimed = 0x100;

    internal static int ClaimReturnIfReady(int state) =>
        CanReturn(state) ? state | ReturnClaimed : state;

    internal static bool IsCompleted(int completion) =>
        completion is CompletionCompletedNormal or
            CompletionCompletedCaller or
            CompletionCompletedTimeout;

    internal static bool IsCancelCompletion(int state)
    {
        var completion = state & CompletionMask;
        return completion is CompletionCompletingCaller or
            CompletionCompletingTimeout or
            CompletionCompletedCaller or
            CompletionCompletedTimeout;
    }

    internal static int Completing(PooledCompletionKind kind) =>
        kind switch
        {
            PooledCompletionKind.Normal => CompletionCompletingNormal,
            PooledCompletionKind.Caller => CompletionCompletingCaller,
            PooledCompletionKind.Timeout => CompletionCompletingTimeout,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Completion kind is not supported.")
        };

    internal static int Completed(PooledCompletionKind kind) =>
        kind switch
        {
            PooledCompletionKind.Normal => CompletionCompletedNormal,
            PooledCompletionKind.Caller => CompletionCompletedCaller,
            PooledCompletionKind.Timeout => CompletionCompletedTimeout,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Completion kind is not supported.")
        };

    internal static bool IsCancel(PooledCompletionKind kind) =>
        kind is PooledCompletionKind.Caller or PooledCompletionKind.Timeout;

    private static bool CanReturn(int state)
    {
        var consumer = state & ConsumerMask;
        return (state & OwnerMask) == OwnerReleased &&
               consumer is ConsumerConsumed or ConsumerAbandoned &&
               IsCompleted(state & CompletionMask);
    }
}
