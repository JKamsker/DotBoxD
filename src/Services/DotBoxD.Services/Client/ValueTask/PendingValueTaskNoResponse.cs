using System.Threading.Tasks.Sources;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Client;

internal sealed class PendingValueTaskNoResponse :
    PooledPendingResponse,
    IValueTaskSource
{
    // Sequential callers use the atomic slot; concurrent returns spill into the locked list.
    private static readonly object PoolGate = new();
    private static PendingValueTaskNoResponse? s_cached;
    private static PendingValueTaskNoResponse? s_overflowPool;

    private ManualResetValueTaskSourceCore<bool> _source;
    private PendingValueTaskNoResponse? _next;

    private PendingValueTaskNoResponse()
    {
        _source.RunContinuationsAsynchronously = true;
    }

    public static PendingValueTaskNoResponse Rent(
        int messageId,
        string service,
        string method)
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

        pending ??= new PendingValueTaskNoResponse();
        pending.Initialize(messageId, service, method);
        return pending;
    }

    public ValueTask ValueTask
    {
        get
        {
            MarkIssued();
            return new ValueTask(this, _source.Version);
        }
    }

    public ValueTask GetDirectValueTask(RpcPeerOutboundInvoker owner)
    {
        MarkIssuedForDirect(owner);
        return new ValueTask(this, _source.Version);
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

                if (payload.Length != 0)
                {
                    throw new ServiceProtocolException(
                        "Response payload is not allowed for a no-response invocation.");
                }
            }
            catch (Exception ex)
            {
                SetError(ex);
                return true;
            }

            CompleteAndSetResult();
        }
        finally
        {
            stream?.Cancel();
            frame.Dispose();
        }

        return true;
    }

    public void GetResult(short token)
    {
        var isCurrentGeneration = token == _source.Version;
        try
        {
            _source.GetResult(token);
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

    private void CompleteAndSetResult()
    {
        var start = BeginResultCompletion();
        if (start == PooledCompletionStart.Rejected)
        {
            return;
        }

        _source.SetResult(true);
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
