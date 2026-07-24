using System.Runtime.ExceptionServices;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;

namespace DotBoxD.Services.Tests.Transport.Receive.TcpPooling;

/// <summary>Keeps every shared TCP receive source leased while a test exercises overflow.</summary>
internal sealed class TcpSaturatedReceivePopulation : IAsyncDisposable
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _cancellation;
    private readonly List<TcpReceiveTestPair> _pairs;
    private readonly ValueTask<RpcFrame>[] _holders;

    private TcpSaturatedReceivePopulation(
        List<TcpReceiveTestPair> pairs,
        CancellationTokenSource cancellation,
        ValueTask<RpcFrame>[] holders)
    {
        _pairs = pairs;
        _cancellation = cancellation;
        _holders = holders;
    }

    public TcpReceiveTestPair OverflowPair => _pairs[^1];

    public static async Task<TcpSaturatedReceivePopulation> CreateAsync()
    {
        var capacity = BoundedTransportOperationPool<object>.MaxRetainedCount;
        var pairs = new List<TcpReceiveTestPair>(capacity + 1);
        var cancellation = new CancellationTokenSource();
        try
        {
            for (var index = 0; index <= capacity; index++)
            {
                pairs.Add(await TcpReceiveTestPair.CreateAsync());
            }

            var holders = pairs
                .Take(capacity)
                .Select(pair => pair.Connection.ReceiveFrameValueAsync(cancellation.Token))
                .ToArray();
            if (holders.Any(static holder => holder.IsCompleted))
            {
                throw new InvalidOperationException(
                    "A shared TCP receive source completed before overflow was established.");
            }

            if (!TcpFrameReceiveOperationPopulation.IsAtCapacity ||
                !TcpFrameReceiveOperationPopulation.HasNoAvailableOperation())
            {
                throw new InvalidOperationException(
                    "The shared TCP receive operation population was not fully leased.");
            }

            return new TcpSaturatedReceivePopulation(pairs, cancellation, holders);
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
