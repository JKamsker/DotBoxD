namespace DotBoxD.Transports.Tcp;

internal sealed class FrameReadTimeoutSource : IDisposable
{
    private CancellationTokenSource? _source;
    private CancellationToken _ownerToken;
    private bool _ownerTokenCanCancel;

    public static TimeSpan Resolve(TimeSpan? timeout, TimeSpan defaultTimeout, string parameterName)
    {
        var value = timeout ?? defaultTimeout;
        if (value == Timeout.InfiniteTimeSpan ||
            (value > TimeSpan.Zero && value.TotalMilliseconds <= int.MaxValue))
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(
            parameterName,
            value,
            "Frame read idle timeout must be positive (at most int.MaxValue ms) or Timeout.InfiniteTimeSpan.");
    }

    public async ValueTask<int> ReadAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken ownerToken,
        TimeSpan timeout)
    {
        var readToken = Start(ownerToken, timeout);
        try
        {
            return await stream.ReadAsync(buffer, readToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (IsTimeoutCancellation(ownerToken))
        {
            throw CreateTimeoutException(timeout);
        }
        finally
        {
            CancelPendingTimeout();
        }
    }

    public CancellationToken Start(CancellationToken ownerToken, TimeSpan timeout)
    {
        var source = EnsureSource(ownerToken);

        try
        {
            source.CancelAfter(timeout);
        }
        catch (ObjectDisposedException)
        {
            source = CreateSource(ownerToken);
            source.CancelAfter(timeout);
        }

        return source.Token;
    }

    // Start establishes the frame owner. Reusing it here keeps ReadExactAsync's state machine to one
    // token while still returning a replacement token if the cached source had to be recreated.
    public CancellationToken Rearm(TimeSpan timeout) => Start(_ownerToken, timeout);

    public bool IsTimeoutCancellation(CancellationToken ownerToken)
    {
        var source = _source;
        return source is not null &&
            source.IsCancellationRequested &&
            !ownerToken.IsCancellationRequested;
    }

    public bool IsCurrentOwnerTimeoutCancellation() => IsTimeoutCancellation(_ownerToken);

    public static IOException CreateTimeoutException(TimeSpan timeout) =>
        new($"Inbound frame read stalled for longer than {timeout} with no data (possible slow-loris peer).");

    public void CancelPendingTimeout()
    {
        var source = _source;
        if (source is { IsCancellationRequested: false })
        {
            try
            {
                source.CancelAfter(Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // Dispose can race a read finally block that is clearing a pending timeout.
            }
        }
    }

    public void Dispose()
    {
        _source?.Dispose();
        _source = null;
    }

    internal void DisposeCurrentSourceForTest() => _source?.Dispose();

    private CancellationTokenSource EnsureSource(CancellationToken ownerToken)
    {
        var source = _source;
        if (source is null ||
            IsDisposed(source) ||
            source.IsCancellationRequested ||
            !MatchesOwner(ownerToken))
        {
            source?.Dispose();
            source = CreateSource(ownerToken);
        }

        return source;
    }

    private CancellationTokenSource CreateSource(CancellationToken ownerToken)
    {
        var source = ownerToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ownerToken)
            : new CancellationTokenSource();
        _source = source;
        _ownerToken = ownerToken;
        _ownerTokenCanCancel = ownerToken.CanBeCanceled;
        return source;
    }

    private static bool IsDisposed(CancellationTokenSource source)
    {
        try
        {
            _ = source.Token;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private bool MatchesOwner(CancellationToken ownerToken) =>
        _ownerTokenCanCancel == ownerToken.CanBeCanceled &&
        (!_ownerTokenCanCancel || _ownerToken.Equals(ownerToken));
}
