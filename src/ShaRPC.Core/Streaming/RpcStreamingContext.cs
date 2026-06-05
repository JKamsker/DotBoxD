using System.IO.Pipelines;
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
    private HashSet<int>? _inboundStreamIds;
    private RpcStreamAttachment? _response;

    public static RpcStreamingContext Disabled { get; } = new();

    private RpcStreamingContext()
    {
    }

    internal RpcStreamingContext(
        RpcStreamManager streams,
        ISerializer serializer,
        CancellationToken ct)
    {
        _streams = streams;
        _serializer = serializer;
        _ct = ct;
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
        var receiver = _streams!.GetOrRegisterInbound(handle, _ct);
        (_inboundStreamIds ??= new HashSet<int>()).Add(receiver.Handle.StreamId);
        return receiver;
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
            throw new InvalidOperationException($"Stream '{handle.StreamId}' is '{handle.Kind}', not '{expected}'.");
        }
    }
}
