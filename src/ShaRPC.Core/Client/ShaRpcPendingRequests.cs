using System.Collections.Concurrent;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;

namespace ShaRPC.Core.Client;

internal sealed class ShaRpcPendingRequests
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<ReceivedResponse>> _requests = new();

    public TaskCompletionSource<ReceivedResponse> Add(int messageId)
    {
        var tcs = new TaskCompletionSource<ReceivedResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_requests.TryAdd(messageId, tcs))
        {
            throw new InvalidOperationException(
                $"A ShaRPC request with message id {messageId} is already pending.");
        }

        return tcs;
    }

    public bool Contains(int messageId) => _requests.ContainsKey(messageId);

    public void Remove(int messageId, Task<ReceivedResponse> task, bool consumed)
    {
        _requests.TryRemove(messageId, out _);
        if (!consumed)
        {
            ReceivedResponse.DisposeWhenAvailable(task);
        }
    }

    public bool TryComplete(
        int messageId,
        RpcResponse response,
        ReadOnlyMemory<byte> payload,
        Payload frame)
    {
        if (!_requests.TryGetValue(messageId, out var tcs))
        {
            return false;
        }

        var received = new ReceivedResponse(response, payload, frame);
        if (!tcs.TrySetResult(received))
        {
            received.Dispose();
        }

        return true;
    }

    public bool TryFail(int messageId, Exception error)
    {
        if (!_requests.TryGetValue(messageId, out var tcs))
        {
            return false;
        }

        tcs.TrySetException(error);
        return true;
    }

    public void FailAll(Exception error)
    {
        foreach (var request in _requests.Values)
        {
            request.TrySetException(error);
        }
    }

    public void CancelAll()
    {
        foreach (var request in _requests.Values)
        {
            request.TrySetCanceled();
        }
    }
}
