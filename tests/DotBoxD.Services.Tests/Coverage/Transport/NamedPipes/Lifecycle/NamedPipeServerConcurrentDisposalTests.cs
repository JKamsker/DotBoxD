using DotBoxD.Transports.NamedPipes;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport;

public sealed class NamedPipeServerConcurrentDisposalTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan EarlyCompletionWindow = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task DisposeAsync_ConcurrentCallAwaitsAndObservesInFlightStopFailure()
    {
        var stopEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var server = new NamedPipeServerTransport(CreatePipeName());
        await server.StartAsync().WaitAsync(Timeout);
        server._beforeStopCancelForTest = async () =>
        {
            stopEntered.SetResult();
            await releaseStop.Task.WaitAsync(Timeout);
            throw new InvalidOperationException("stop failed");
        };

        var firstDispose = server.DisposeAsync().AsTask();
        Task? secondDispose = null;
        try
        {
            await stopEntered.Task.WaitAsync(Timeout);

            secondDispose = server.DisposeAsync().AsTask();
            var secondCompletedBeforeStopFinished = await CompletesWithinAsync(secondDispose, EarlyCompletionWindow);

            releaseStop.SetResult();

            var firstFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => firstDispose.WaitAsync(Timeout));
            var secondFailure = await Record.ExceptionAsync(() => secondDispose!.WaitAsync(Timeout));
            var lateFailure = await Record.ExceptionAsync(
                () => server.DisposeAsync().AsTask().WaitAsync(Timeout));

            Assert.Equal("stop failed", firstFailure.Message);
            Assert.False(
                secondCompletedBeforeStopFinished,
                "The second concurrent DisposeAsync completed before StopAsync finished.");
            var stopFailure = Assert.IsType<InvalidOperationException>(secondFailure);
            Assert.Equal("stop failed", stopFailure.Message);
            var lateStopFailure = Assert.IsType<InvalidOperationException>(lateFailure);
            Assert.Equal("stop failed", lateStopFailure.Message);
        }
        finally
        {
            releaseStop.TrySetResult();
            await Record.ExceptionAsync(() => firstDispose.WaitAsync(Timeout));
            if (secondDispose is not null)
            {
                await Record.ExceptionAsync(() => secondDispose.WaitAsync(Timeout));
            }
        }
    }

    private static async Task<bool> CompletesWithinAsync(Task task, TimeSpan timeout) =>
        await Task.WhenAny(task, Task.Delay(timeout)) == task;

    private static string CreatePipeName() => "dotboxd-test-" + Guid.NewGuid().ToString("N");
}
