namespace DotBoxD.Services.Streaming.Core;

internal static class RpcStreamIdSequence
{
    internal static int Next(ref int counter)
    {
        while (true)
        {
            var streamId = Interlocked.Increment(ref counter);
            if (streamId > 0)
            {
                return streamId;
            }

            // Skip the negative range after overflow without rewinding another caller's progress.
            Interlocked.CompareExchange(ref counter, 0, streamId);
        }
    }
}
