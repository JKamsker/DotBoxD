using System.Buffers;
using System.Text;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Http.Internal;

internal static class SafeHttpBodyReader
{
    private const int ReadBufferSize = 4096;
    private const int InitialBodyBufferCapacity = 256;

    public static async ValueTask<SafeHttpLimitedText> ReadLimitedTextAsync(
        SandboxContext context,
        HttpResponseMessage response,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > 0 and var contentLength && contentLength > maxBytes)
        {
            throw QuotaExceeded();
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var readBuffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
            var bodyBuffer = ArrayPool<byte>.Shared.Rent(InitialBodyCapacity(response.Content.Headers.ContentLength, maxBytes));
            var bodyLength = 0;
            try
            {
                while (true)
                {
                    var remaining = maxBytes - bodyLength;
                    var readLimit = remaining >= readBuffer.Length ? readBuffer.Length : (int)remaining + 1;
                    var read = await stream.ReadAsync(readBuffer.AsMemory(0, readLimit), cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    context.Budget.ChargeNetworkRead(read);
                    if (read > remaining)
                    {
                        throw QuotaExceeded();
                    }

                    context.ChargeAllocation(read);
                    EnsureBodyCapacity(ref bodyBuffer, bodyLength + read);
                    Buffer.BlockCopy(readBuffer, 0, bodyBuffer, bodyLength, read);
                    bodyLength += read;
                }

                context.ChargeFuel(bodyLength);
                context.ChargeStringAllocation(Encoding.UTF8.GetCharCount(bodyBuffer, 0, bodyLength));
                var text = Encoding.UTF8.GetString(bodyBuffer, 0, bodyLength);
                context.RecordStringReturnCredit(text);
                return new SafeHttpLimitedText(text, bodyLength);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(readBuffer, clearArray: true);
                ArrayPool<byte>.Shared.Return(bodyBuffer, clearArray: true);
            }
        }
    }

    private static int InitialBodyCapacity(long? contentLength, long maxBytes)
    {
        var expectedLength = contentLength is > 0 ? contentLength.Value : maxBytes;
        return expectedLength is > 0 and < InitialBodyBufferCapacity
            ? CheckedLength(expectedLength)
            : InitialBodyBufferCapacity;
    }

    private static void EnsureBodyCapacity(ref byte[] buffer, int required)
    {
        if (required <= buffer.Length)
        {
            return;
        }

        var nextLength = buffer.Length;
        do
        {
            nextLength *= 2;
        }
        while (nextLength < required);

        var next = ArrayPool<byte>.Shared.Rent(nextLength);
        Buffer.BlockCopy(buffer, 0, next, 0, buffer.Length);
        ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        buffer = next;
    }

    private static int CheckedLength(long length)
    {
        if (length > int.MaxValue)
        {
            throw QuotaExceeded();
        }

        return (int)length;
    }

    private static SandboxRuntimeException QuotaExceeded()
        => new(new SandboxError(
            SandboxErrorCode.QuotaExceeded,
            "net.http.get denied: response exceeds byte limit"));
}

internal readonly record struct SafeHttpLimitedText(string Text, long BytesRead);
