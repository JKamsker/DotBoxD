namespace DotBoxD.Services.Transport;

/// <summary>
/// Coordinates one producer and one ValueTask consumer without resetting the source while
/// either side is still using it.
/// </summary>
internal struct PooledValueTaskSourceLifecycle
{
    private const int ConsumerMask = 0x03;
    private const int ConsumerIssued = 0x00;
    private const int ConsumerReading = 0x01;
    private const int ConsumerConsumed = 0x02;
    private const int ProducerFinished = 0x04;
    private const int ReturnClaimed = 0x08;

    private int _state;

    public void Initialize() => Volatile.Write(ref _state, ConsumerIssued);

    public bool TryBeginReading() =>
        TryTransition(ConsumerMask, ConsumerIssued, ConsumerReading, claimReturn: false);

    public void RollBackReading()
    {
        if (!TryTransition(ConsumerMask, ConsumerReading, ConsumerIssued, claimReturn: false))
        {
            throw new InvalidOperationException(
                "Pooled ValueTask consumer rollback transition is invalid.");
        }
    }

    public bool FinishReading()
    {
        if (!TryTransition(
                ConsumerMask,
                ConsumerReading,
                ConsumerConsumed,
                claimReturn: true,
                out var returnClaimed))
        {
            throw new InvalidOperationException(
                "Pooled ValueTask consumer completion transition is invalid.");
        }

        return returnClaimed;
    }

    public bool FinishProducer()
    {
        while (true)
        {
            var state = Volatile.Read(ref _state);
            if ((state & (ProducerFinished | ReturnClaimed)) != 0)
            {
                throw new InvalidOperationException(
                    "Pooled ValueTask producer completion transition is invalid.");
            }

            var next = state | ProducerFinished;
            if ((state & ConsumerMask) == ConsumerConsumed)
            {
                next |= ReturnClaimed;
            }

            if (Interlocked.CompareExchange(ref _state, next, state) == state)
            {
                return (next & ReturnClaimed) != 0;
            }
        }
    }

    private bool TryTransition(
        int mask,
        int expected,
        int replacement,
        bool claimReturn) =>
        TryTransition(mask, expected, replacement, claimReturn, out _);

    private bool TryTransition(
        int mask,
        int expected,
        int replacement,
        bool claimReturn,
        out bool returnClaimed)
    {
        while (true)
        {
            var state = Volatile.Read(ref _state);
            if ((state & ReturnClaimed) != 0 || (state & mask) != expected)
            {
                returnClaimed = false;
                return false;
            }

            var next = (state & ~mask) | replacement;
            if (claimReturn &&
                replacement == ConsumerConsumed &&
                (state & ProducerFinished) != 0)
            {
                next |= ReturnClaimed;
            }

            if (Interlocked.CompareExchange(ref _state, next, state) == state)
            {
                returnClaimed = (next & ReturnClaimed) != 0;
                return true;
            }
        }
    }
}
