using DotBoxD.Services.Exceptions;

namespace DotBoxD.Services.Client;

internal sealed partial class RpcPeerOutboundInvoker
{
    private PendingReceivedResponse ReservePendingRequest(CancellationToken ct)
    {
        if (!TryEnterPendingSlot())
        {
            throw new ServiceException("Maximum pending requests reached.");
        }

        try
        {
            for (var attempts = 0; attempts < _maxPendingRequests; attempts++)
            {
                ct.ThrowIfCancellationRequested();
                var messageId = NextMessageId(ct);
                if (messageId != 0 && _pending.TryAdd(messageId, out var pending))
                {
                    return pending;
                }
            }

            throw new ServiceException("Unable to reserve a request message id.");
        }
        catch
        {
            ReleasePendingSlot();
            throw;
        }
    }

    private PendingUnaryResponse<TResponse> ReservePendingUnaryRequest<TResponse>(
        string service,
        string method,
        CancellationToken ct)
    {
        if (!TryEnterPendingSlot())
        {
            throw new ServiceException("Maximum pending requests reached.");
        }

        try
        {
            for (var attempts = 0; attempts < _maxPendingRequests; attempts++)
            {
                ct.ThrowIfCancellationRequested();
                var messageId = NextMessageId(ct);
                if (messageId != 0 &&
                    _pending.TryAddUnary<TResponse>(
                        messageId,
                        ct.CanBeCanceled,
                        _hasFiniteTimeout,
                        ct,
                        service,
                        method,
                        out var pending))
                {
                    return pending;
                }
            }

            throw new ServiceException("Unable to reserve a request message id.");
        }
        catch
        {
            ReleasePendingSlot();
            throw;
        }
    }

    private PendingValueTaskUnaryResponse<TResponse> ReservePendingValueTaskUnaryRequest<TResponse>(
        string service,
        string method,
        CancellationToken ct)
    {
        if (!TryEnterPendingSlot())
        {
            throw new ServiceException("Maximum pending requests reached.");
        }

        try
        {
            for (var attempts = 0; attempts < _maxPendingRequests; attempts++)
            {
                ct.ThrowIfCancellationRequested();
                var messageId = NextMessageId(ct);
                if (messageId != 0 &&
                    _pending.TryAddValueTaskUnary<TResponse>(messageId, service, method, out var pending))
                {
                    return pending;
                }
            }

            throw new ServiceException("Unable to reserve a request message id.");
        }
        catch
        {
            ReleasePendingSlot();
            throw;
        }
    }

    private PendingValueTaskNoResponse ReservePendingValueTaskNoResponseRequest(
        string service,
        string method,
        CancellationToken ct)
    {
        if (!TryEnterPendingSlot())
        {
            throw new ServiceException("Maximum pending requests reached.");
        }

        try
        {
            for (var attempts = 0; attempts < _maxPendingRequests; attempts++)
            {
                ct.ThrowIfCancellationRequested();
                var messageId = NextMessageId(ct);
                if (messageId != 0 &&
                    _pending.TryAddValueTaskNoResponse(messageId, service, method, out var pending))
                {
                    return pending;
                }
            }

            throw new ServiceException("Unable to reserve a request message id.");
        }
        catch
        {
            ReleasePendingSlot();
            throw;
        }
    }

    private bool TryEnterPendingSlot()
    {
        if (Interlocked.Increment(ref _pendingCount) <= _maxPendingRequests)
        {
            return true;
        }

        Interlocked.Decrement(ref _pendingCount);
        return false;
    }

    private void ReleasePendingSlot() =>
        Interlocked.Decrement(ref _pendingCount);

    private void CompletePooledSetupFailure(PooledPendingResponse pending)
    {
        var removed = _pending.Remove(pending.MessageId, pending, consumed: false);
        if (removed)
        {
            pending.CompleteAbandonedAfterRemoval();
        }

        ReleasePendingSlot();
        pending.ReleaseSetup();
    }

    private void CompletePooledWrapper(
        PooledPendingResponse pending,
        bool consumed)
    {
        // Terminal producers remove correlation before signaling the source. Once the wrapper
        // consumes that signal, another locked lookup can only miss.
        if (!consumed)
        {
            var removed = _pending.Remove(pending.MessageId, pending, consumed: false);
            if (removed)
            {
                pending.CompleteAbandonedAfterRemoval();
            }
        }

        ReleasePendingSlot();
        pending.ReleaseWrapper();
    }

    internal void CompleteUnaryPending(IPendingResponse pending, bool sendCancel)
    {
        if (sendCancel)
        {
            _cancelFrames.TrySend(pending.MessageId);
        }

        ReleasePendingSlot();
    }

    private int NextMessageId(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var messageId = Interlocked.Increment(ref _messageIdCounter);
            if (messageId != 0)
            {
                return messageId;
            }
        }
    }
}
