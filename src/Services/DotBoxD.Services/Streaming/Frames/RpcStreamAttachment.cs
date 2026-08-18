using System.Buffers;
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
        return new StreamAttachment(handle, stream, leaveOpen);
    }

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
        return new PipeAttachment(handle, pipe, completeReader);
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
        return new AsyncEnumerableAttachment<T>(handle, source);
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

    private sealed class StreamAttachment : RpcStreamAttachment
    {
        private const int ChunkSize = 64 * 1024;
        private readonly Stream _stream;
        private readonly bool _leaveOpen;

        public StreamAttachment(RpcStreamHandle handle, Stream stream, bool leaveOpen)
            : base(handle)
        {
            _stream = stream;
            _leaveOpen = leaveOpen;
        }

        internal override async Task PumpCoreAsync(
            RpcStreamManager streams,
            ISerializer serializer,
            CancellationToken ct)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
            Exception? pumpFailure = null;
            try
            {
                while (true)
                {
                    var read = await _stream.ReadAsync(buffer.AsMemory(0, ChunkSize), ct).ConfigureAwait(false);
                    if (read == 0)
                    {
                        return;
                    }

                    await streams.SendStreamItemAsync(Handle.StreamId, buffer.AsMemory(0, read), ct)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                pumpFailure = ex;
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                await DisposeSourceAfterPumpAsync(pumpFailure).ConfigureAwait(false);
            }
        }

        private protected override ValueTask DisposeSourceCoreAsync() =>
            _leaveOpen ? default : DisposeStreamAsync(_stream);

        private protected override bool OwnsSource => !_leaveOpen;
    }

    private sealed class PipeAttachment : RpcStreamAttachment
    {
        private readonly Pipe _pipe;
        private readonly bool _completeReader;

        public PipeAttachment(RpcStreamHandle handle, Pipe pipe, bool completeReader)
            : base(handle)
        {
            _pipe = pipe;
            _completeReader = completeReader;
        }

        internal override async Task PumpCoreAsync(
            RpcStreamManager streams,
            ISerializer serializer,
            CancellationToken ct)
        {
            Exception? pumpFailure = null;
            try
            {
                while (true)
                {
                    var result = await _pipe.Reader.ReadAsync(ct).ConfigureAwait(false);
                    var buffer = result.Buffer;
                    try
                    {
                        if (result.IsCanceled)
                        {
                            return;
                        }

                        foreach (var segment in buffer)
                        {
                            if (!segment.IsEmpty)
                            {
                                await streams.SendStreamItemAsync(Handle.StreamId, segment, ct).ConfigureAwait(false);
                            }
                        }
                    }
                    finally
                    {
                        _pipe.Reader.AdvanceTo(buffer.End);
                    }

                    if (result.IsCompleted)
                    {
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                pumpFailure = ex;
                throw;
            }
            finally
            {
                await DisposeSourceAfterPumpAsync(pumpFailure).ConfigureAwait(false);
            }
        }

        private protected override ValueTask DisposeSourceCoreAsync() =>
            _completeReader ? _pipe.Reader.CompleteAsync() : default;

        private protected override bool OwnsSource => _completeReader;
    }

    private sealed class AsyncEnumerableAttachment<T> : RpcStreamAttachment
    {
        private readonly IAsyncEnumerable<T> _source;

        public AsyncEnumerableAttachment(RpcStreamHandle handle, IAsyncEnumerable<T> source)
            : base(handle) =>
            _source = source;

        internal override async Task PumpCoreAsync(
            RpcStreamManager streams,
            ISerializer serializer,
            CancellationToken ct)
        {
            await foreach (var item in _source.WithCancellation(ct).ConfigureAwait(false))
            {
                await streams.SendStreamItemAsync(Handle.StreamId, item, serializer, ct).ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask DisposeStreamAsync(Stream stream)
    {
        if (stream is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            return;
        }

        stream.Dispose();
    }
}
