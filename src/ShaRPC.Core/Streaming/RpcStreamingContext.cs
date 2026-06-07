using System.IO.Pipelines;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;

namespace ShaRPC.Core.Streaming;

/// <summary>
/// Runtime implementation of <see cref="IRpcStreamingContext"/>.
/// </summary>
public sealed class RpcStreamingContext : IRpcStreamingContext
{
    private readonly RpcStreamManager? _streams;
    private readonly ISerializer? _serializer;
    private readonly CancellationToken _ct;
    private readonly Dictionary<int, RpcStreamKind>? _declaredInboundStreams;
    private HashSet<int>? _claimedInboundStreamIds;
    private HashSet<int>? _inboundStreamIds;
    private RpcStreamAttachment? _response;

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
        _declaredInboundStreams = CreateDeclaredInboundStreams(declaredInboundStreams);
    }

    internal RpcStreamAttachment? Response => _response;

    internal int[]? AcquiredInboundStreamIds => _inboundStreamIds?.ToArray();

    internal async ValueTask AbandonResponseAsync()
    {
        if (Interlocked.Exchange(ref _response, null) is not { } response)
        {
            return;
        }

        _streams?.ReleaseOutboundReservation(response.Handle.StreamId);
        await response.DisposeSourceBestEffortAsync("Streaming response cleanup failed").ConfigureAwait(false);
    }

    public Stream GetStream(RpcStreamHandle handle)
    {
        return new RpcRemoteStream(GetInbound(handle, RpcStreamKind.Binary));
    }

    public Pipe GetPipe(RpcStreamHandle handle)
    {
        return RpcPipeBridge.CreateReadablePipe(GetInbound(handle, RpcStreamKind.Binary), _ct);
    }

    public IAsyncEnumerable<T> GetAsyncEnumerable<T>(RpcStreamHandle handle)
    {
        return new RpcRemoteAsyncEnumerable<T>(
            GetInbound(handle, RpcStreamKind.Items),
            _serializer!);
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

    private RpcStreamReceiver GetInbound(RpcStreamHandle handle, RpcStreamKind expected)
    {
        EnsureEnabled();
        EnsureKind(handle, expected);
        ClaimDeclaredInbound(handle);
        var receiver = _streams!.GetRegisteredInbound(handle);
        (_inboundStreamIds ??= new HashSet<int>()).Add(receiver.Handle.StreamId);
        return receiver;
    }

    private void ClaimDeclaredInbound(RpcStreamHandle handle)
    {
        if (_declaredInboundStreams is null ||
            !_declaredInboundStreams.TryGetValue(handle.StreamId, out var declaredKind))
        {
            throw new ShaRpcProtocolException(
                $"Inbound stream id '{handle.StreamId}' was not declared by the request.");
        }

        if (declaredKind != handle.Kind)
        {
            throw new ShaRpcProtocolException(
                $"Inbound stream id '{handle.StreamId}' was declared as '{declaredKind}', not '{handle.Kind}'.");
        }

        if (!(_claimedInboundStreamIds ??= new HashSet<int>()).Add(handle.StreamId))
        {
            throw new ShaRpcProtocolException(
                $"Inbound stream id '{handle.StreamId}' was already claimed.");
        }
    }

    private void EnsureEnabled()
    {
        if (_streams is null)
        {
            throw new InvalidOperationException("This dispatch path does not support streaming.");
        }
    }

    private static void EnsureKind(RpcStreamHandle handle, RpcStreamKind expected)
    {
        if (handle.Kind != expected)
        {
            throw new ShaRpcProtocolException($"Stream '{handle.StreamId}' is '{handle.Kind}', not '{expected}'.");
        }
    }

    private static Dictionary<int, RpcStreamKind>? CreateDeclaredInboundStreams(
        RpcStreamHandle[]? handles)
    {
        if (handles is null || handles.Length == 0)
        {
            return null;
        }

        var declared = new Dictionary<int, RpcStreamKind>(handles.Length);
        foreach (var handle in handles)
        {
            declared.Add(handle.StreamId, handle.Kind);
        }

        return declared;
    }
}
