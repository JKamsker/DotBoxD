using System.Diagnostics.Metrics;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Tests.Support;
using Xunit;

namespace DotBoxD.Services.Tests.Diagnostics;

[Collection(RpcTelemetryCollection.Name)]
public sealed class RpcPeerStartPublicationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ConcurrentStart_WaitsForCompleteStartupPublication()
    {
        var startedMeasurementEntered =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStartedMeasurement =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = CreateBlockingPeerStartedListener(
            startedMeasurementEntered,
            releaseStartedMeasurement);
        var (leftConnection, rightConnection) = InMemoryPipe.CreateConnectionPair();
        var peer = RpcPeer.Over(leftConnection, new MessagePackRpcSerializer());
        Task<RpcPeer>? firstStart = null;

        try
        {
            firstStart = Task.Run(peer.Start);
            await startedMeasurementEntered.Task.WaitAsync(Timeout);

            var secondStartEntered =
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondStart = Task.Run(() =>
            {
                secondStartEntered.TrySetResult();
                return peer.Start();
            });
            await secondStartEntered.Task.WaitAsync(Timeout);

            var prematureCompletion = await Task.WhenAny(secondStart, Task.Delay(100));
            Assert.NotSame(secondStart, prematureCompletion);

            releaseStartedMeasurement.TrySetResult();
            Assert.Same(peer, await firstStart.WaitAsync(Timeout));
            Assert.Same(peer, await secondStart.WaitAsync(Timeout));
        }
        finally
        {
            releaseStartedMeasurement.TrySetResult();
            if (firstStart is not null)
            {
                await firstStart.WaitAsync(Timeout);
            }

            await peer.DisposeAsync();
            await rightConnection.DisposeAsync();
        }
    }

    private static MeterListener CreateBlockingPeerStartedListener(
        TaskCompletionSource entered,
        TaskCompletionSource release)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == RpcTelemetry.InstrumentationName &&
                instrument.Name == "dotboxd.rpc.peers.active")
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
        {
            if (measurement == 1 && entered.TrySetResult())
            {
                release.Task.Wait(Timeout);
            }
        });
        listener.Start();
        return listener;
    }
}
