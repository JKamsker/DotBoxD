using DotBoxD.Plugins.Runtime.Lifecycle;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

public sealed class LiveStateSyncRegistryTests
{
    [Fact]
    public async Task Register_during_input_sync_is_visible_on_the_next_sync()
    {
        var registry = new LiveStateSyncRegistry(_ => LiveUpdateMode.Sync);
        var syncStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSync = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCalls = 0;
        var secondCalls = 0;

        registry.Register(typeof(FirstState), () =>
        {
            firstCalls++;
            syncStarted.TrySetResult();
            releaseSync.Task.GetAwaiter().GetResult();
        });

        var sync = Task.Run(() => registry.SynchronizeForInput());
        await syncStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var registration = Task.Run(() => registry.Register(typeof(SecondState), () => secondCalls++));
        try
        {
            await registration.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseSync.TrySetResult();
        }

        Assert.Empty(await sync.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, firstCalls);
        Assert.Equal(0, secondCalls);

        Assert.Empty(registry.SynchronizeForInput());
        Assert.Equal(2, firstCalls);
        Assert.Equal(1, secondCalls);
    }

    [Fact]
    public async Task Register_during_flush_is_visible_on_the_next_flush()
    {
        var registry = new LiveStateSyncRegistry(_ => LiveUpdateMode.AsyncSet);
        var syncStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSync = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCalls = 0;
        var secondCalls = 0;

        registry.Register(typeof(FirstState), () =>
        {
            firstCalls++;
            syncStarted.TrySetResult();
            releaseSync.Task.GetAwaiter().GetResult();
        });

        var flush = Task.Run(registry.SynchronizeForFlush);
        await syncStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var registration = Task.Run(() => registry.Register(typeof(SecondState), () => secondCalls++));
        try
        {
            await registration.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseSync.TrySetResult();
        }

        await flush.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, firstCalls);
        Assert.Equal(0, secondCalls);

        registry.SynchronizeForFlush();
        Assert.Equal(2, firstCalls);
        Assert.Equal(1, secondCalls);
    }

    [Fact]
    public void Input_sync_filters_async_updates_and_returns_caller_owned_lists()
    {
        var registry = new LiveStateSyncRegistry(
            stateType => stateType == typeof(FirstState) ? LiveUpdateMode.Sync : LiveUpdateMode.AsyncSet);
        var syncCalls = 0;
        var firstAsyncCalls = 0;
        var secondAsyncCalls = 0;
        registry.Register(typeof(FirstState), () => syncCalls++);
        registry.Register(typeof(SecondState), () => firstAsyncCalls++);

        var firstDeferredUpdates = registry.SynchronizeForInput();
        registry.Register(typeof(ThirdState), () => secondAsyncCalls++);
        var secondDeferredUpdates = registry.SynchronizeForInput();

        Assert.Equal(2, syncCalls);
        Assert.Single(firstDeferredUpdates);
        Assert.Equal(2, secondDeferredUpdates.Count);
        Assert.NotSame(firstDeferredUpdates, secondDeferredUpdates);

        firstDeferredUpdates[0]();
        Assert.Equal(1, firstAsyncCalls);
        Assert.Equal(0, secondAsyncCalls);

        registry.SynchronizeForFlush();
        Assert.Equal(2, syncCalls);
        Assert.Equal(2, firstAsyncCalls);
        Assert.Equal(1, secondAsyncCalls);
    }

    [Fact]
    public async Task Concurrent_registrations_are_all_published()
    {
        var registry = new LiveStateSyncRegistry(_ => LiveUpdateMode.Sync);
        var calls = new int[StateTypes.Length];
        var registrations = new Task[StateTypes.Length];
        for (var i = 0; i < registrations.Length; i++)
        {
            var index = i;
            registrations[index] = Task.Run(
                () => registry.Register(StateTypes[index], () => Interlocked.Increment(ref calls[index])));
        }

        await Task.WhenAll(registrations);

        Assert.Empty(registry.SynchronizeForInput());
        Assert.All(calls, callCount => Assert.Equal(1, callCount));
    }

    private static readonly Type[] StateTypes =
    [
        typeof(FirstState),
        typeof(SecondState),
        typeof(ThirdState),
        typeof(FourthState),
        typeof(FifthState),
        typeof(SixthState),
        typeof(SeventhState),
        typeof(EighthState)
    ];

    private sealed class FirstState;
    private sealed class SecondState;
    private sealed class ThirdState;
    private sealed class FourthState;
    private sealed class FifthState;
    private sealed class SixthState;
    private sealed class SeventhState;
    private sealed class EighthState;
}
