using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace DotBoxD.Services.Buffers;

/// <summary>
/// Tracks one reusable writer's current lease without letting stale frame aliases return a newer
/// lease. The low bits describe the current transition; the remaining bits identify the lease.
/// </summary>
internal struct PooledBufferWriterLease
{
    private const long StateMask = 3;
    private const long Active = 0;
    private const long Detaching = 1;
    private const long Detached = 2;
    private const long Returned = 3;
    private const long FirstToken = 4;

    private long _state;

    public static PooledBufferWriterLease Create() => new() { _state = FirstToken };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long CaptureActiveToken()
    {
        var state = Volatile.Read(ref _state);
        if ((state & StateMask) != Active)
        {
            throw new ObjectDisposedException(nameof(PooledBufferWriter));
        }

        return state;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsActive(long token) => Volatile.Read(ref _state) == token;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReturn(long token) =>
        Interlocked.CompareExchange(ref _state, token | Returned, token | Active) == token;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryBeginFrameDetach(long token) =>
        Interlocked.CompareExchange(ref _state, token | Detaching, token | Active) == token;

    public void CompleteFrameDetach(long token)
    {
        var previous = Interlocked.CompareExchange(
            ref _state,
            token | Returned,
            token | Detaching);
        if (previous != (token | Detaching))
        {
            throw new InvalidOperationException("The writer lease changed during frame detachment.");
        }
    }

    public long BeginPublicDetach()
    {
        var state = Volatile.Read(ref _state);
        if ((state & StateMask) != Active ||
            Interlocked.CompareExchange(ref _state, state | Detaching, state) != state)
        {
            throw new InvalidOperationException("Buffer has already been detached or disposed.");
        }

        return state & ~StateMask;
    }

    public void CompletePublicDetach(long token) =>
        Volatile.Write(ref _state, token | Detached);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReturnCurrent()
    {
        var state = Volatile.Read(ref _state);
        var status = state & StateMask;
        if (status == Active || status == Detached)
        {
            if (Interlocked.CompareExchange(ref _state, state | Returned, state) == state)
            {
                return true;
            }
        }
        else if (status == Returned)
        {
            return false;
        }

        return TryReturnCurrentContended(state & ~StateMask);
    }

    public void Reset()
    {
        var state = _state;
        Debug.Assert((state & StateMask) == Returned, "Only a returned writer lease can be reset.");
        var token = unchecked((state & ~StateMask) + FirstToken);
        if (token == 0)
        {
            token = FirstToken;
        }

        Volatile.Write(ref _state, token);
    }

    private bool TryReturnCurrentContended(long token)
    {
        var spinner = new SpinWait();

        while (true)
        {
            var state = Volatile.Read(ref _state);
            if ((state & ~StateMask) != token)
            {
                return false;
            }

            switch (state & StateMask)
            {
                case Active:
                case Detached:
                    if (Interlocked.CompareExchange(ref _state, token | Returned, state) == state)
                    {
                        return true;
                    }

                    break;
                case Detaching:
                    spinner.SpinOnce();
                    break;
                default:
                    return false;
            }
        }
    }
}
