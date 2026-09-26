using System.Reflection;
using DotBoxD.Transports.NamedPipes;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport;

public sealed class NamedPipeServerLifecycleRaceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Start_waiting_for_lifecycle_lock_cannot_restart_disposed_server()
    {
        await using var server = new NamedPipeServerTransport(CreatePipeName());
        try
        {
            var start = RunWhileBlocked(server, () => server.StartAsync(), () =>
            {
                Assert.True(server.DisposeAsync().IsCompletedSuccessfully);
            });

            await Assert.ThrowsAsync<ObjectDisposedException>(() => start.WaitAsync(Timeout));
            Assert.Equal(0, server.StartedForTest);
            Assert.Null(server.StopCtsForTest);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Stop_waiting_for_lifecycle_lock_cannot_allow_replacement_start()
    {
        await using var server = new NamedPipeServerTransport(CreatePipeName());
        await server.StartAsync();
        using var originalSource = server.StopCtsForTest;
        Assert.NotNull(originalSource);

        var stop = RunWhileBlocked(server, () => server.StopAsync(), () =>
        {
            Assert.Throws<InvalidOperationException>(() => { _ = server.StartAsync(); });
            Assert.Same(originalSource, server.StopCtsForTest);
        });

        await stop.WaitAsync(Timeout);
        Assert.Equal(0, server.StartedForTest);
        Assert.Null(server.StopCtsForTest);
        Assert.Throws<ObjectDisposedException>(() => originalSource.Token);

        await server.StartAsync();
        Assert.Equal(1, server.StartedForTest);
        Assert.NotNull(server.StopCtsForTest);
    }

    private static async Task RunWhileBlocked(NamedPipeServerTransport server, Func<Task> operation, Action whileBlocked)
    {
        var gate = typeof(NamedPipeServerTransport)
            .GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(server)!;
        using var entered = new ManualResetEventSlim();
        Thread? worker = null;
        Task pending;
        Exception? interleavedFailure;
        lock (gate)
        {
            pending = Task.Run(() =>
            {
                worker = Thread.CurrentThread;
                entered.Set();
                return operation();
            });
            Assert.True(entered.Wait(Timeout));
            Assert.NotNull(worker);
            // Start/stop have no other blocking operation before entering the lifecycle monitor.
            Assert.True(SpinWait.SpinUntil(
                () => (worker.ThreadState & ThreadState.WaitSleepJoin) != 0, Timeout));
            Assert.False(pending.IsCompleted);

            interleavedFailure = Record.Exception(whileBlocked);
        }

        Assert.Null(interleavedFailure);
        await pending.WaitAsync(Timeout);
    }

    private static string CreatePipeName() => "dotboxd-lifecycle-" + Guid.NewGuid().ToString("N");
}
