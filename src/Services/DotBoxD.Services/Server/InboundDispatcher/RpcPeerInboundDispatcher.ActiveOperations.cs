using DotBoxD.Services.Diagnostics;

namespace DotBoxD.Services.Server;

internal sealed partial class RpcPeerInboundDispatcher
{
    private bool TryEnterActiveRequest() =>
        TryEnterActiveOperation(ref _activeRequestCount, ref _activeRequestsDrained);

    private bool TryEnterActiveStream() =>
        TryEnterActiveOperation(ref _activeStreamCount, ref _activeStreamsDrained);

    // A response stream outlives its dispatch slot, so the remote caller could otherwise hold
    // unbounded pump state by never reading or canceling the streams it requests.
    private bool TryReserveResponseStream()
    {
        if (Interlocked.Increment(ref _responseStreamCount) <= _maxResponseStreams)
        {
            return true;
        }

        Interlocked.Decrement(ref _responseStreamCount);
        return false;
    }

    private void ReleaseResponseStream() => Interlocked.Decrement(ref _responseStreamCount);

    private async Task SendResponseStreamLimitErrorAsync(int messageId)
    {
        try
        {
            using var errorFrame = _responseBuilder.BuildErrorFrame(messageId, RpcErrors.ResponseStreamLimit());
            await _sendAsync(errorFrame.Memory, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Response stream limit error notification failed", ex);
        }
    }

    private bool TryEnterActiveOperation(
        ref int count,
        ref TaskCompletionSource<bool>? drained)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return false;
        }

        Interlocked.Increment(ref count);
        if (Volatile.Read(ref _stopped) == 0)
        {
            return true;
        }

        CompleteActiveOperation(ref count, ref drained);
        return false;
    }

    private Task WaitForActiveRequestsAsync() =>
        WaitForActiveOperationsAsync(ref _activeRequestCount, ref _activeRequestsDrained);

    private Task WaitForActiveStreamsAsync() =>
        WaitForActiveOperationsAsync(ref _activeStreamCount, ref _activeStreamsDrained);

    private static Task WaitForActiveOperationsAsync(
        ref int count,
        ref TaskCompletionSource<bool>? drained)
    {
        if (Volatile.Read(ref count) == 0)
        {
            return Task.CompletedTask;
        }

        var signal = Volatile.Read(ref drained);
        if (signal is null)
        {
            var created = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            signal = Interlocked.CompareExchange(ref drained, created, null) ?? created;
        }

        if (Volatile.Read(ref count) == 0)
        {
            signal.TrySetResult(true);
        }

        return signal.Task;
    }

    private void CompleteActiveRequest() =>
        CompleteActiveOperation(ref _activeRequestCount, ref _activeRequestsDrained);

    private void CompleteActiveStream() =>
        CompleteActiveOperation(ref _activeStreamCount, ref _activeStreamsDrained);

    private static void CompleteActiveOperation(
        ref int count,
        ref TaskCompletionSource<bool>? drained)
    {
        if (Interlocked.Decrement(ref count) == 0)
        {
            Volatile.Read(ref drained)?.TrySetResult(true);
        }
    }
}
