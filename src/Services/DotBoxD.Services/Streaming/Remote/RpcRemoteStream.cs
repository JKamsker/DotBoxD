using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;

namespace DotBoxD.Services.Streaming.Remote;

internal sealed class RpcRemoteStream : Stream
{
    private RpcStreamReceiver? _receiver;
    private RpcStreamChunk? _current;
    private int _offset;
    private int _disposed;

    public RpcRemoteStream(RpcStreamReceiver receiver) => _receiver = receiver;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ValidateBuffer(buffer, offset, count);
        return await ReadCoreAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        ReadCoreAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            var receiver = Interlocked.Exchange(ref _receiver, null)!;
            try
            {
                receiver.Cancel();
            }
            finally
            {
                Interlocked.Exchange(ref _current, null)?.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private async ValueTask<int> ReadCoreAsync(Memory<byte> buffer, CancellationToken ct)
    {
        var receiver = Volatile.Read(ref _receiver);
        if (Volatile.Read(ref _disposed) != 0 || receiver is null)
        {
            throw new ObjectDisposedException(nameof(RpcRemoteStream));
        }

        if (buffer.Length == 0)
        {
            return 0;
        }

        ct.ThrowIfCancellationRequested();

        var current = Volatile.Read(ref _current);
        while (current is null || _offset >= current.Payload.Length)
        {
            Interlocked.Exchange(ref _current, null)?.Dispose();
            current = await receiver.ReadChunkAsync(ct).ConfigureAwait(false);
            Interlocked.Exchange(ref _current, current);
            // A read waking during disposal must not restore the receiver through its chunk.
            if (Volatile.Read(ref _disposed) != 0)
            {
                Interlocked.Exchange(ref _current, null)?.DisposeWithoutCredit();
                throw new ObjectDisposedException(nameof(RpcRemoteStream));
            }

            _offset = 0;
            if (current is null)
            {
                return 0;
            }
        }

        var source = current.Payload.Slice(_offset);
        var count = Math.Min(buffer.Length, source.Length);
        source.Slice(0, count).CopyTo(buffer);
        _offset += count;
        return count;
    }

    private static void ValidateBuffer(byte[] buffer, int offset, int count)
    {
        if (buffer is null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if ((uint)offset > (uint)buffer.Length || (uint)count > (uint)(buffer.Length - offset))
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
    }
}
