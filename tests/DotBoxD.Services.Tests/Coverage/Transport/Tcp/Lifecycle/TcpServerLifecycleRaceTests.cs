using System.Net;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport;

public sealed class TcpServerLifecycleRaceTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Stop_completed_before_publication_cancels_start_and_releases_the_listener()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        server._onListenerStartedBeforePublishForTest = () => server.StopAsync().GetAwaiter().GetResult();

        await Assert.ThrowsAsync<OperationCanceledException>(() => server.StartAsync());

        Assert.Null(server.LocalEndpoint);
        server._onListenerStartedBeforePublishForTest = null;
        await server.StartAsync().WaitAsync(Guard);
        Assert.NotNull(server.LocalEndpoint);
    }

    [Fact]
    public async Task Stop_during_start_does_not_leave_a_published_listener()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        using var listenerStarted = new ManualResetEventSlim();
        using var releaseStart = new ManualResetEventSlim();
        using var stopEntered = new ManualResetEventSlim();
        server._onListenerStartedBeforePublishForTest = () =>
        {
            listenerStarted.Set();
            Assert.True(releaseStart.Wait(TimeSpan.FromSeconds(30)));
        };

        var start = Task.Run(() => server.StartAsync());
        Task? stop = null;
        Thread? stopWorker = null;
        try
        {
            Assert.True(listenerStarted.Wait(Guard));
            stop = Task.Run(() =>
            {
                stopWorker = Thread.CurrentThread;
                stopEntered.Set();
                return server.StopAsync();
            });
            Assert.True(stopEntered.Wait(Guard));
            Assert.NotNull(stopWorker);
            // Stop must either finish or wait for startup before the listener is published.
            Assert.True(SpinWait.SpinUntil(
                () => stop.IsCompleted || (stopWorker.ThreadState & ThreadState.WaitSleepJoin) != 0,
                Guard));
        }
        finally
        {
            releaseStart.Set();
            await (stop is null ? start : Task.WhenAll(start, stop)).WaitAsync(Guard);
        }

        Assert.Null(server.LocalEndpoint);

        await server.StartAsync().WaitAsync(Guard);
        Assert.NotNull(server.LocalEndpoint);
    }
}
