using System.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;

namespace DotBoxD.Services.Streaming.Frames;

internal sealed class RpcBinaryStreamAttachment : RpcStreamAttachment
{
    private const int ChunkSize = 64 * 1024;
    private Stream? _stream;
    private readonly bool _leaveOpen;

    public RpcBinaryStreamAttachment(RpcStreamHandle handle, Stream stream, bool leaveOpen)
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
        var stream = _stream;
        if (stream is null)
        {
            return;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
        Exception? pumpFailure = null;
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, ChunkSize), ct).ConfigureAwait(false);
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

    private protected override async ValueTask DisposeSourceCoreAsync()
    {
        if (_leaveOpen)
        {
            return;
        }

        try
        {
            if (_stream is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                _stream!.Dispose();
            }
        }
        finally
        {
            _stream = null;
        }
    }

    private protected override bool OwnsSource => !_leaveOpen;
}
