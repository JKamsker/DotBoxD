using System.IO.Pipelines;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;

namespace DotBoxD.Services.Streaming.Remote;

/// <summary>
/// Runtime implementation of <see cref="IRpcStreamingContext"/>.
/// </summary>
public sealed class RpcStreamingContext : IRpcStreamingContext
{
    private RpcStreamManager? _streams;
    private ISerializer? _serializer;
    private CancellationToken _ct;
    private RpcInboundStreamClaims? _inboundClaims;
    private readonly object _gate = new();
    private RpcStreamAttachment? _response;
    private bool _completed;

    public static RpcStreamingContext Disabled { get; } = new();

    private RpcStreamingContext()
    {
    }

    internal RpcStreamingContext(
        RpcStreamManager streams,
        ISerializer serializer,
        CancellationToken ct,
        RpcStreamHandle[]? declaredInboundStreams = null)
    {
        _streams = streams;
        _serializer = serializer;
        _ct = ct;
        _inboundClaims = RpcInboundStreamClaims.Create(declaredInboundStreams);
    }

    internal RpcStreamAttachment? Response
    {
        get
        {
            lock (_gate)
            {
                return _response;
            }
        }
    }

    internal void EnsureAllDeclaredInboundStreamsClaimed()
    {
        if (ReferenceEquals(this, Disabled))
        {
            return;
        }

        lock (_gate)
        {
            _inboundClaims?.EnsureAllClaimed();
        }
    }

    internal async ValueTask AbandonResponseAsync()
    {
        if (ReferenceEquals(this, Disabled))
        {
            return;
        }

        RpcStreamManager? streams;
        RpcStreamAttachment? response;
        lock (_gate)
        {
            _completed = true;
            streams = _streams;
            response = _response;
            ClearReferences();
        }

        if (response is null)
        {
            return;
        }

        streams?.ReleaseOutboundReservation(response.Handle.StreamId);
        await response.DisposeSourceBestEffortAsync("Streaming response cleanup failed").ConfigureAwait(false);
    }

    internal RpcStreamAttachment? CompleteDispatch()
    {
        // Disabled is shared by every non-streaming dispatch and can never hold a response.
        // Avoid serializing unrelated peers on its otherwise unnecessary monitor.
        if (ReferenceEquals(this, Disabled))
        {
            return null;
        }

        lock (_gate)
        {
            _completed = true;
            var response = _response;
            if (response is null)
            {
                ClearReferences();
            }

            return response;
        }
    }

    internal void ReleaseResponseOwnership()
    {
        if (ReferenceEquals(this, Disabled))
        {
            return;
        }

        lock (_gate)
        {
            _completed = true;
            ClearReferences();
        }
    }

    public Stream GetStream(RpcStreamHandle handle)
    {
        return new RpcRemoteStream(GetInbound(handle, RpcStreamKind.Binary).Receiver);
    }

    public Pipe GetPipe(RpcStreamHandle handle)
    {
        var inbound = GetInbound(handle, RpcStreamKind.Binary);
        return RpcPipeBridge.CreateReadablePipe(inbound.Receiver, inbound.Cancellation);
    }

    public IAsyncEnumerable<T> GetAsyncEnumerable<T>(RpcStreamHandle handle)
    {
        var inbound = GetInbound(handle, RpcStreamKind.Items);
        return new RpcRemoteAsyncEnumerable<T>(inbound.Receiver, inbound.Serializer);
    }

    public void SetResponse(Stream stream)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        SetResponse(
            RpcStreamKind.Binary,
            handle => RpcStreamAttachment.FromStream(handle, stream, leaveOpen: false));
    }

    public void SetResponse(Pipe pipe)
    {
        if (pipe is null)
        {
            throw new ArgumentNullException(nameof(pipe));
        }

        SetResponse(
            RpcStreamKind.Binary,
            handle => RpcStreamAttachment.FromPipe(handle, pipe, completeReader: true));
    }

    public void SetResponse<T>(IAsyncEnumerable<T> items)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        SetResponse(
            RpcStreamKind.Items,
            handle => RpcStreamAttachment.FromAsyncEnumerable(handle, items));
    }

    private void SetResponse(
        RpcStreamKind kind,
        Func<RpcStreamHandle, RpcStreamAttachment> createResponse)
    {
        EnsureEnabled();

        lock (_gate)
        {
            EnsureDispatchActive();
            _ct.ThrowIfCancellationRequested();
            if (_response is not null)
            {
                throw new InvalidOperationException("Only one streamed response can be set for an RPC call.");
            }

            var handle = _streams!.ReserveOutbound(kind);
            try
            {
                _response = createResponse(handle);
            }
            catch
            {
                _streams.RemoveOutbound(handle.StreamId);
                throw;
            }
        }
    }

    private (RpcStreamReceiver Receiver, ISerializer Serializer, CancellationToken Cancellation) GetInbound(
        RpcStreamHandle handle, RpcStreamKind expected)
    {
        EnsureEnabled();
        RpcStreamValidation.ValidateHandleArgument(handle, nameof(handle));
        EnsureKind(handle, expected);

        lock (_gate)
        {
            EnsureDispatchActive();
            _ct.ThrowIfCancellationRequested();
            ClaimDeclaredInbound(handle);
            return (_streams!.GetRegisteredInbound(handle), _serializer!, _ct);
        }
    }

    private void ClaimDeclaredInbound(RpcStreamHandle handle)
    {
        var claims = _inboundClaims;
        if (claims is null)
        {
            throw new ServiceProtocolException(
                $"Inbound stream id '{handle.StreamId}' was not declared by the request.");
        }

        claims.Claim(handle);
    }

    private void EnsureEnabled()
    {
        if (ReferenceEquals(this, Disabled))
        {
            throw new InvalidOperationException("This dispatch path does not support streaming.");
        }
    }

    private void ClearReferences()
    {
        // The caller holds _gate and has finished or transferred response ownership.
        _streams = null;
        _serializer = null;
        _ct = default;
        _inboundClaims = null;
        _response = null;
    }

    private void EnsureDispatchActive()
    {
        if (_completed)
        {
            throw new InvalidOperationException("This streaming context has already completed dispatch.");
        }
    }

    private static void EnsureKind(RpcStreamHandle handle, RpcStreamKind expected)
    {
        if (handle.Kind != expected)
        {
            throw new ServiceProtocolException($"Stream '{handle.StreamId}' is '{handle.Kind}', not '{expected}'.");
        }
    }

}
