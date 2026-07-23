using System.Threading.Tasks.Sources;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Client;

internal sealed class PendingValueTaskUnaryResponse<TResponse> :
    PooledPendingResponse,
    IValueTaskSource<TResponse>
{
    // Sequential callers use the atomic slot; concurrent returns spill into the locked list.
    private static readonly object PoolGate = new();
    private static PendingValueTaskUnaryResponse<TResponse>? s_cached;
    private static PendingValueTaskUnaryResponse<TResponse>? s_overflowPool;

    private ManualResetValueTaskSourceCore<TResponse> _source;
    private PendingValueTaskUnaryResponse<TResponse>? _next;

    private PendingValueTaskUnaryResponse()
    {
        // Do not resume user code on the transport read loop. A continuation can make a synchronous
        // RPC-facing call, and inline completion would block the same loop needed to deliver its response.
        _source.RunContinuationsAsynchronously = true;
    }

    public static PendingValueTaskUnaryResponse<TResponse> Rent(
        int messageId,
        string service,
        string method,
        RpcPeerOutboundInvoker owner,
        CancellationToken callerToken)
    {
        var pending = Interlocked.Exchange(ref s_cached, null);
        if (pending is null)
        {
            lock (PoolGate)
            {
                pending = s_overflowPool;
                if (pending is not null)
                {
                    s_overflowPool = pending._next;
                    pending._next = null;
                }
            }
        }

        pending ??= new PendingValueTaskUnaryResponse<TResponse>();
        pending.Initialize(messageId, service, method, owner, callerToken);
        return pending;
    }

    public ValueTask<TResponse> ValueTask
    {
        get
        {
            MarkIssued();
            return new ValueTask<TResponse>(this, _source.Version);
        }
    }

    public ValueTask<TResponse> GetDirectValueTask(RpcPeerOutboundInvoker owner)
    {
        MarkIssuedForDirect(owner);
        return new ValueTask<TResponse>(this, _source.Version);
    }

    public override bool TrySetResponse(
        RpcResponse response,
        ReadOnlyMemory<byte> payload,
        RpcFrame frame,
        RpcStreamReceiver? stream,
        ISerializer serializer)
    {
        try
        {
            TResponse result;
            try
            {
                if (!response.IsSuccess)
                {
                    throw new RemoteServiceException(
                        response.ErrorMessage ?? "Unknown error",
                        response.ErrorType ?? "Unknown");
                }

                if (response.Stream is not null)
                {
                    throw new ServiceProtocolException(
                        "Response opened a stream for a non-streaming invocation.");
                }

                result = serializer.Deserialize<TResponse>(payload);
                if (TryCancelIfCallerCanceledAfterMaterialization())
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                SetError(ex);
                return true;
            }

            CompleteAndSetResult(result);
        }
        finally
        {
            stream?.Cancel();
            frame.Dispose();
        }

        return true;
    }

    public TResponse GetResult(short token)
    {
        var isCurrentGeneration = token == _source.Version;
        try
        {
            return _source.GetResult(token);
        }
        catch
        {
            if (isCurrentGeneration &&
                _source.GetStatus(token) == ValueTaskSourceStatus.Pending)
            {
                isCurrentGeneration = false;
            }

            throw;
        }
        finally
        {
            if (isCurrentGeneration)
            {
                MarkConsumed();
            }
        }
    }

    public ValueTaskSourceStatus GetStatus(short token) =>
        _source.GetStatus(token);

    public void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags) =>
        _source.OnCompleted(continuation, state, token, flags);

    private void CompleteAndSetResult(TResponse response)
    {
        var start = BeginResultCompletion();
        if (start == PooledCompletionStart.Rejected)
        {
            return;
        }

        _source.SetResult(response);
        FinishResultCompletion(start);
    }

    protected override void SetExceptionCore(Exception error) =>
        _source.SetException(error);

    protected override void ResetSourceCore() =>
        _source.Reset();

    protected override void PushToPoolCore()
    {
        if (Interlocked.CompareExchange(ref s_cached, this, null) is null)
        {
            return;
        }

        lock (PoolGate)
        {
            _next = s_overflowPool;
            s_overflowPool = this;
        }
    }
}
