namespace DotBoxD.Services.Protocol;

internal static class StreamReadOperations
{
    public static async ValueTask<int> ReadExactAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.Slice(totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return totalRead;
            }

            totalRead += read;
        }

        return totalRead;
    }
}
