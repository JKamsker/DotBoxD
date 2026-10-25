using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Blazor.Tests.Rendering;

public sealed class BlazorRendererDisposalTerminalSurpriseTests
{
    [Fact]
    public async Task Authorization_cancellation_callback_can_join_renderer_disposal()
    {
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer();
        await using var session = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), renderer);
        var authorizer = new DisposalJoiningAuthorizer(renderer);
        var input = new UiInput(NodeId: 1, PropertyId: UiPropertyId.Text, Value: UiValue.FromString("value"));
        var submit = renderer.SubmitAsync(session, input, UiFixture.User(), authorizer).AsTask();
        await authorizer.Registered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await renderer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => submit);
        Assert.True(authorizer.Joined);
    }

    private sealed class DisposalJoiningAuthorizer(BlazorUiRenderer renderer) : IUiInteractionAuthorizer
    {
        public TaskCompletionSource Registered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Joined { get; private set; }
        public ValueTask<bool> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, UiSession session,
            UiInput input, CancellationToken cancellationToken)
        {
            cancellationToken.Register(() => Joined = renderer.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(1)));
            Registered.TrySetResult();
            return new ValueTask<bool>(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ContinueWith(
                static _ => false, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));
        }
    }

    [Fact]
    public async Task Cancellation_failure_still_notifies_viewer_and_is_shared_by_later_callers()
    {
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer();
        var session = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), renderer);
        var authorizer = new ThrowingCancellationAuthorizer();
        var input = new UiInput(NodeId: 1, PropertyId: UiPropertyId.Text, Value: UiValue.FromString("value"));
        var submit = renderer.SubmitAsync(session, input, UiFixture.User(), authorizer).AsTask();
        await authorizer.Registered.Task;
        var notifications = 0;
        using var subscription = renderer.Subscribe(() =>
        {
            notifications++;
            throw new InvalidOperationException("notification sentinel");
        });

        var error = await Assert.ThrowsAsync<AggregateException>(() => renderer.DisposeAsync().AsTask());
        var repeated = await Assert.ThrowsAsync<AggregateException>(() => renderer.DisposeAsync().AsTask());
        Assert.Same(error, repeated);
        Assert.Contains(error.Flatten().InnerExceptions, e => e.Message == "cancellation sentinel");
        Assert.Equal(1, notifications);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => submit);
        try
        { await session.DisposeAsync(); }
        catch (AggregateException) { }
    }

    [Fact]
    public async Task Detached_callback_work_observes_terminal_disposal_failure()
    {
        var renderer = new BlazorUiRenderer();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? detached = null;
        using var subscription = renderer.Subscribe(() =>
        {
            detached = Task.Run(async () =>
            {
                await release.Task;
                await renderer.DisposeAsync();
            });
            throw new InvalidOperationException("viewer sentinel");
        });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => renderer.DisposeAsync().AsTask());
        release.TrySetResult();
        var repeated = await Assert.ThrowsAsync<InvalidOperationException>(() => detached!.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(error, repeated);
    }

    private sealed class ThrowingCancellationAuthorizer : IUiInteractionAuthorizer
    {
        public TaskCompletionSource Registered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<bool> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, UiSession session,
            UiInput input, CancellationToken cancellationToken)
        {
            cancellationToken.Register(static () => throw new InvalidOperationException("cancellation sentinel"));
            Registered.TrySetResult();
            return new ValueTask<bool>(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ContinueWith(
                static _ => false, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));
        }
    }

    [Fact]
    public async Task Async_viewer_teardown_can_join_its_own_session_disposal()
    {
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer();
        var session = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), renderer);
        using var subscription = renderer.Subscribe(async () =>
        {
            await Task.Yield();
            await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        });
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(session.IsDisconnected);
        Assert.True(renderer.IsDisposed);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_authorizer_callback_can_join_the_owner_session(bool forward)
    {
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer();
        var session = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), renderer);
        var authorizer = new OwnerJoiningAuthorizer(forward);
        var submission = renderer.SubmitAsync(session, new UiInput(NodeId: 1, PropertyId: UiPropertyId.Text, Value: UiValue.FromString("edit")),
            new System.Security.Claims.ClaimsPrincipal(), authorizer).AsTask();
        await authorizer.Registered.Task;
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => submission);
        Assert.True(session.IsDisconnected);
    }

    private sealed class OwnerJoiningAuthorizer(bool forward) : IUiInteractionAuthorizer
    {
        public TaskCompletionSource Registered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<bool> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, UiSession session,
            UiInput input, CancellationToken cancellationToken)
        {
            cancellationToken.Register(() =>
            {
                var disposal = forward ? Task.Run(async () => await session.DisposeAsync()) : session.DisposeAsync().AsTask();
                disposal.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
            });
            Registered.TrySetResult();
            return new ValueTask<bool>(new TaskCompletionSource<bool>().Task);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposal_callback_can_join_disposal_without_reentering_or_waiting_on_itself(bool asynchronous)
    {
        var renderer = new BlazorUiRenderer();
        var calls = 0;
        using var subscription = renderer.Subscribe(async () =>
        {
            Interlocked.Increment(ref calls);
            if (asynchronous)
            { await Task.Yield(); }
            await renderer.DisposeAsync();
        });

        await renderer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, calls);
        Assert.True(renderer.IsDisposed);
    }

    [Fact]
    public async Task Concurrent_disposal_waits_for_the_viewer_teardown_shared_by_the_first_call()
    {
        var renderer = new BlazorUiRenderer();
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCalls = 0;
        using var subscription = renderer.Subscribe(async () =>
        {
            Interlocked.Increment(ref callbackCalls);
            callbackEntered.TrySetResult();
            await releaseCallback.Task;
        });

        var firstDisposal = renderer.DisposeAsync().AsTask();
        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var concurrentDisposal = renderer.DisposeAsync().AsTask();

        try
        { Assert.False(concurrentDisposal.IsCompleted); }
        finally
        {
            releaseCallback.TrySetResult();
            await Task.WhenAll(firstDisposal, concurrentDisposal).WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(1, callbackCalls);
    }
}
