using System.Runtime.ExceptionServices;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Tests.Transport.Receive.StreamPooling;

/// <summary>Keeps every shared Stream receive source leased while a test exercises overflow.</summary>
internal sealed class StreamSaturatedReceivePopulation : IAsyncDisposable
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _cancellation;
    private readonly List<StreamReceiveTestPair> _pairs;
    private readonly ValueTask<RpcFrame>[] _holders;

    private StreamSaturatedReceivePopulation(
        List<StreamReceiveTestPair> pairs,
        CancellationTokenSource cancellation,
        ValueTask<RpcFrame>[] holders)
    {
        _pairs = pairs;
        _cancellation = cancellation;
        _holders = holders;
    }

    public StreamReceiveTestPair OverflowPair => _pairs[^1];

    public static async Task<StreamSaturatedReceivePopulation> CreateAsync(
        TimeSpan? overflowIdleTimeout = null)
    {
        var capacity = BoundedTransportOperationPool<object>.MaxRetainedCount;
        var pairs = new List<StreamReceiveTestPair>(capacity + 1);
        var cancellation = new CancellationTokenSource();
        try
        {
            for (var index = 0; index <= capacity; index++)
            {
                pairs.Add(await StreamReceiveTestPair.CreateAsync(
                    index == capacity ? overflowIdleTimeout : Timeout.InfiniteTimeSpan));
            }

            var holders = pairs
                .Take(capacity)
                .Select(pair => pair.Connection.ReceiveFrameValueAsync(cancellation.Token))
                .ToArray();
            if (holders.Any(static holder => holder.IsCompleted))
            {
                throw new InvalidOperationException(
                    "A shared Stream receive source completed before overflow was established.");
            }

            if (!StreamFrameReceiveOperationPopulation.IsAtCapacity ||
                StreamFrameReceiveOperation.HasAvailableOperationForPopulation)
            {
                throw new InvalidOperationException(
                    "The shared Stream receive operation population was not fully leased.");
            }

            return new StreamSaturatedReceivePopulation(pairs, cancellation, holders);
        }
        catch
        {
            cancellation.Cancel();
            cancellation.Dispose();
            foreach (var pair in pairs)
            {
                await pair.DisposeAsync();
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        Exception? firstError = null;
        foreach (var holder in _holders)
        {
            try
            {
                using var frame = await holder.AsTask().WaitAsync(Guard);
            }
            catch (OperationCanceledException)
            {
                // Expected while releasing the shared operation population.
            }
            catch (Exception error)
            {
                firstError ??= error;
            }
        }

        foreach (var pair in _pairs)
        {
            try
            {
                await pair.DisposeAsync();
            }
            catch (Exception error)
            {
                firstError ??= error;
            }
        }

        _cancellation.Dispose();
        if (firstError is not null)
        {
            ExceptionDispatchInfo.Capture(firstError).Throw();
        }
    }
}
