using static DotBoxD.Services.Client.PooledPendingStates;

namespace DotBoxD.Services.Client;

internal struct PooledPendingLifecycle
{
    private int _state;

    public PendingCancellationKind CancellationKind
        => (Volatile.Read(ref _state) & CompletionMask) switch
        {
            CompletionCompletingCaller or CompletionCompletedCaller => PendingCancellationKind.Caller,
            CompletionCompletingTimeout or CompletionCompletedTimeout => PendingCancellationKind.Timeout,
            _ => PendingCancellationKind.None
        };

    public bool CompletionStarted =>
        (Volatile.Read(ref _state) & CompletionMask) != CompletionActive;

    public void Initialize() =>
        Volatile.Write(
            ref _state,
            OwnerSetup | ConsumerUnissued | CompletionActive);

    public void TransferSetupToWrapper(PooledPendingResponse pending) =>
        TransitionRequired(pending, OwnerMask, OwnerSetup, OwnerWrapper);

    public void ReleaseSetup(PooledPendingResponse pending) =>
        TransitionRequired(pending, OwnerMask, OwnerSetup, OwnerReleased);

    public void ReleaseWrapper(PooledPendingResponse pending) =>
        TransitionRequired(pending, OwnerMask, OwnerWrapper, OwnerReleased);

    public void MarkIssued(PooledPendingResponse pending) =>
        TransitionRequired(pending, ConsumerMask, ConsumerUnissued, ConsumerIssued);

    public void MarkIssuedForDirect(PooledPendingResponse pending)
    {
        TransitionRequired(
            pending,
            OwnerMask | ConsumerMask,
            OwnerSetup | ConsumerUnissued,
            OwnerDirect | ConsumerIssued);
        TryReleaseDirect(pending);
    }

    public void MarkConsumed(PooledPendingResponse pending) =>
        TransitionRequired(pending, ConsumerMask, ConsumerIssued, ConsumerConsumed);

    public void MarkAbandoned(PooledPendingResponse pending) =>
        TransitionRequired(pending, ConsumerMask, ConsumerUnissued, ConsumerAbandoned);

    public PooledCompletionStart BeginCompletion(
        PooledPendingResponse pending,
        PooledCompletionKind kind)
    {
        var completing = Completing(kind);
        while (true)
        {
            var state = Volatile.Read(ref _state);
            if ((state & ReturnClaimed) != 0 ||
                (state & CompletionMask) != CompletionActive)
            {
                return PooledCompletionStart.Rejected;
            }

            var releaseDirect = (state & OwnerMask) == OwnerDirect;
            var directIssued = releaseDirect &&
                (state & ConsumerMask) == ConsumerIssued;
            if (directIssued)
            {
                // PendingRequests removes the entry before invoking a completion producer, so this
                // direct path has exclusive producer ownership. Publish completion only after the
                // source has finished signaling; otherwise a fast continuation could recycle and
                // reset the source while ManualResetValueTaskSourceCore is still completing it.
                pending.NotifyDirectOwner(IsCancel(kind));
                return PooledCompletionStart.DirectAfterSource;
            }

            var next = (state & ~CompletionMask) | completing;
            if (releaseDirect)
            {
                next = (next & ~OwnerMask) | OwnerDirectReleasing;
            }

            if (Interlocked.CompareExchange(ref _state, next, state) != state)
            {
                continue;
            }

            if (releaseDirect)
            {
                pending.NotifyDirectOwner(IsCancel(kind));
            }
            else
            {
                TryReleaseDirect(pending);
            }

            return PooledCompletionStart.TwoPhase;
        }
    }

    public void FinishCompletion(
        PooledPendingResponse pending,
        PooledCompletionKind kind,
        PooledCompletionStart start)
    {
        if (start == PooledCompletionStart.DirectAfterSource)
        {
            FinishDirectCompletion(pending, kind);
            return;
        }

        if (start != PooledCompletionStart.TwoPhase)
        {
            throw new InvalidOperationException("Pooled pending completion was not started.");
        }

        FinishTwoPhaseCompletion(pending, kind);
    }

    private void FinishTwoPhaseCompletion(
        PooledPendingResponse pending,
        PooledCompletionKind kind)
    {
        var completing = Completing(kind);
        var completed = Completed(kind);
        while (true)
        {
            var state = Volatile.Read(ref _state);
            if ((state & ReturnClaimed) != 0 ||
                (state & CompletionMask) != completing)
            {
                throw new InvalidOperationException("Pooled pending completion transition is invalid.");
            }

            var next = (state & ~CompletionMask) | completed;
            if ((state & OwnerMask) == OwnerDirectReleasing)
            {
                next = (next & ~OwnerMask) | OwnerReleased;
            }

            next = ClaimReturnIfReady(next);
            if (Interlocked.CompareExchange(ref _state, next, state) != state)
            {
                continue;
            }

            RecycleIfClaimed(pending, next);
            return;
        }
    }

    private void FinishDirectCompletion(
        PooledPendingResponse pending,
        PooledCompletionKind kind)
    {
        while (true)
        {
            var state = Volatile.Read(ref _state);
            if ((state & ReturnClaimed) != 0 ||
                (state & OwnerMask) != OwnerDirect ||
                (state & CompletionMask) != CompletionActive)
            {
                throw new InvalidOperationException("Pooled direct completion transition is invalid.");
            }

            var next = (state & ~(OwnerMask | CompletionMask)) |
                OwnerReleased |
                Completed(kind);
            next = ClaimReturnIfReady(next);
            if (Interlocked.CompareExchange(ref _state, next, state) != state)
            {
                continue;
            }

            RecycleIfClaimed(pending, next);
            return;
        }
    }

    private void TryReleaseDirect(PooledPendingResponse pending)
    {
        while (true)
        {
            var state = Volatile.Read(ref _state);
            if ((state & ReturnClaimed) != 0 ||
                (state & OwnerMask) != OwnerDirect ||
                (state & CompletionMask) == CompletionActive)
            {
                return;
            }

            var releasing = (state & ~OwnerMask) | OwnerDirectReleasing;
            if (Interlocked.CompareExchange(ref _state, releasing, state) != state)
            {
                continue;
            }

            pending.NotifyDirectOwner(IsCancelCompletion(releasing));
            ReleaseDirectAfterCompletedResponse(pending);
            return;
        }
    }

    private void ReleaseDirectAfterCompletedResponse(PooledPendingResponse pending)
    {
        var state = Volatile.Read(ref _state);
        if ((state & OwnerMask) == OwnerDirectReleasing &&
            IsCompleted(state & CompletionMask))
        {
            TransitionRequired(
                pending,
                OwnerMask,
                OwnerDirectReleasing,
                OwnerReleased);
        }
    }

    private bool TryTransition(
        PooledPendingResponse pending,
        int mask,
        int expected,
        int replacement)
    {
        while (true)
        {
            var state = Volatile.Read(ref _state);
            if ((state & ReturnClaimed) != 0 || (state & mask) != expected)
            {
                return false;
            }

            var next = (state & ~mask) | replacement;
            next = ClaimReturnIfReady(next);
            if (Interlocked.CompareExchange(ref _state, next, state) != state)
            {
                continue;
            }

            RecycleIfClaimed(pending, next);
            return true;
        }
    }

    private void TransitionRequired(
        PooledPendingResponse pending,
        int mask,
        int expected,
        int replacement)
    {
        if (!TryTransition(pending, mask, expected, replacement))
        {
            throw new InvalidOperationException("Pooled pending response lifecycle transition is invalid.");
        }
    }

    private static void RecycleIfClaimed(PooledPendingResponse pending, int state)
    {
        if ((state & ReturnClaimed) != 0)
        {
            pending.RecycleClaimed();
        }
    }
}
