using System.IO.Pipelines;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;

namespace DotBoxD.Services.Streaming.Frames;

/// <summary>
/// A local source that will be streamed over an RPC request or response.
/// </summary>
public abstract class RpcStreamAttachment
{
    private int _outboundRegistrationClaimed;
    private int _sourceDisposed;

    private protected RpcStreamAttachment(RpcStreamHandle handle) => Handle = handle;

    public RpcStreamHandle Handle { get; }

    public static RpcStreamAttachment FromStream(
        RpcStreamHandle handle,
        Stream stream,
        bool leaveOpen = true)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        RequireHandle(handle, RpcStreamKind.Binary);
        return new RpcBinaryStreamAttachment(handle, stream, leaveOpen);
    }

    /// <summary>
    /// Streams from a pipe, splitting large segments into bounded chunks. When
    /// <paramref name="completeReader"/> is false, cancellation or a failed send leaves the
    /// reader open and unsent bytes available to the caller. Only chunks whose send completed
    /// successfully are consumed. Writer backpressure remains
    /// until the caller consumes or discards retained bytes.
    /// </summary>
    public static RpcStreamAttachment FromPipe(
        RpcStreamHandle handle,
        Pipe pipe,
        bool completeReader = false)
    {
        if (pipe is null)
        {
            throw new ArgumentNullException(nameof(pipe));
        }

        RequireHandle(handle, RpcStreamKind.Binary);
        return new RpcPipeAttachment(handle, pipe, completeReader);
    }

    public static RpcStreamAttachment FromAsyncEnumerable<T>(
        RpcStreamHandle handle,
        IAsyncEnumerable<T> source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        RequireHandle(handle, RpcStreamKind.Items);
        return new RpcAsyncEnumerableAttachment<T>(handle, source);
    }

    internal abstract Task PumpCoreAsync(
        RpcStreamManager streams,
        ISerializer serializer,
        CancellationToken ct);

    internal bool TryClaimOutboundRegistration() =>
        Interlocked.CompareExchange(ref _outboundRegistrationClaimed, 1, 0) == 0;

    internal void ReleaseOutboundRegistration() =>
        Volatile.Write(ref _outboundRegistrationClaimed, 0);

    // Releases the owned source exactly once, whether the call comes from the pump's own finally or
    // from a sibling stream's best-effort cleanup while this pump has already completed. The set owns
    // the source (leaveOpen:false / completeReader:true), so disposing it twice would violate the
    // single-ownership contract for a caller-supplied, non-idempotent source.
    internal ValueTask DisposeSourceOnceAsync() =>
        Interlocked.Exchange(ref _sourceDisposed, 1) == 0 ? DisposeSourceCoreAsync() : default;

    internal void ThrowIfOwnedSourceDisposed()
    {
        if (OwnsSource && Volatile.Read(ref _sourceDisposed) != 0)
        {
            throw new ServiceProtocolException("An owned stream attachment cannot be reused after its source is disposed.");
        }
    }

    private protected virtual ValueTask DisposeSourceCoreAsync() => default;

    private protected virtual bool OwnsSource => false;

    internal async ValueTask DisposeSourceBestEffortAsync(string operation)
    {
        try
        {
            await DisposeSourceOnceAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report(operation, ex);
        }
    }

    internal async ValueTask DisposeSourceAfterPumpAsync(Exception? pumpFailure)
    {
        try
        {
            await DisposeSourceOnceAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (pumpFailure is not null)
        {
            RpcDiagnostics.Report("Outbound stream source cleanup failed", ex);
        }
    }

    private static void RequireHandle(RpcStreamHandle handle, RpcStreamKind expected)
    {
        if (handle.StreamId <= 0)
        {
            throw new ArgumentException("Stream handle stream id must be positive.", nameof(handle));
        }

        if (handle.Kind != expected)
        {
            throw new ArgumentException($"Stream handle kind must be {expected}.", nameof(handle));
        }
    }
}
