using System.Runtime.CompilerServices;

namespace DotBoxD.Services.Buffers;

/// <summary>Owns the bounded object and small-buffer caches used by internal frame writers.</summary>
internal static class PooledBufferWriterPool
{
    private const int MaxRetainedBufferLength = 4096;
    [ThreadStatic]
    private static PooledBufferWriter? s_cachedWriter;
    private static readonly object GlobalOverflowGate = new();
    private static PooledBufferWriter? s_globalCachedWriter;
    private static PooledBufferWriter? s_globalOverflowWriter;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PooledBufferWriter Rent(int initialCapacity, int maxWritten)
    {
        var writer = TakeCachedWriter();
        if (writer is null)
        {
            writer = PooledBufferWriter.CreatePooled(initialCapacity, maxWritten);
        }
        else
        {
            var retainedBuffer = writer.CachedResource as byte[];
            writer.CachedResource = null;
            writer.ResetForRent(retainedBuffer, initialCapacity, maxWritten);
        }

        writer.CacheThreadId = Environment.CurrentManagedThreadId;
        return writer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(PooledBufferWriter writer, byte[]? buffer)
    {
        if (buffer is { Length: > MaxRetainedBufferLength })
        {
            PooledBufferWriter.ReturnBuffer(buffer);
            buffer = null;
        }

        writer.PrepareForCache();
        var retainBuffer = buffer is not null;
        if (writer.CacheThreadId == Environment.CurrentManagedThreadId &&
            s_cachedWriter is null)
        {
            if (retainBuffer)
            {
                writer.CachedResource = buffer;
            }

            s_cachedWriter = writer;
            return;
        }

        if (retainBuffer)
        {
            writer.CachedResource = buffer;
        }

        if (Interlocked.CompareExchange(ref s_globalCachedWriter, writer, null) is null)
        {
            return;
        }

        ReturnOverflow(writer, buffer, retainBuffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PooledBufferWriter? TakeCachedWriter()
    {
        var writer = s_cachedWriter;
        if (writer is not null)
        {
            s_cachedWriter = null;
            return writer;
        }

        writer = Interlocked.Exchange(ref s_globalCachedWriter, null);
        if (writer is not null)
        {
            return writer;
        }

        return TakeOverflowWriter();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReturnOverflow(
        PooledBufferWriter writer,
        byte[]? buffer,
        bool retainBuffer)
    {
        if (retainBuffer)
        {
            writer.CachedResource = null;
            PooledBufferWriter.ReturnBuffer(buffer);
        }

        lock (GlobalOverflowGate)
        {
            writer.CachedResource = s_globalOverflowWriter;
            s_globalOverflowWriter = writer;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PooledBufferWriter? TakeOverflowWriter()
    {
        lock (GlobalOverflowGate)
        {
            var writer = s_globalOverflowWriter;
            if (writer is not null)
            {
                s_globalOverflowWriter = writer.CachedResource as PooledBufferWriter;
                writer.CachedResource = null;
            }

            return writer;
        }
    }
}
