using System.Collections.Immutable;
using System.Threading.Channels;
using DotBoxD.UI;
using DotBoxD.UI.Runtime;

namespace DotBoxD.Kernels.Tests.UI.Lifecycle;

public sealed class UiMalformedRemoteInputTests
{
    [Fact]
    public async Task Malformed_remote_input_is_recorded_without_disconnecting_or_blocking_the_next_remote_input()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var renderer = new QueuedInputRenderer();
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remote = new TestUiTransport((request, _) =>
        {
            dispatched.TrySetResult();
            return ValueTask.FromResult(UiTestFixture.Reply(request));
        });
        var policy = new UiPolicy { MaxInFlightRemoteEvents = 1 };
        await using var session = await UiTestFixture.Host(sandbox, policy).InstallAsync(UiTestFixture.Counter(), renderer, remote);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        renderer.Enqueue(new UiInput(EventId: 2, NodeId: 5));
        renderer.Enqueue(new UiInput(EventId: 2));

        await dispatched.Task.WaitAsync(timeout.Token);

        Assert.Equal("Malformed semantic UI input.", session.LastInputError);
        Assert.False(session.IsDisconnected);
        Assert.Same(renderer, session.Renderer);
        Assert.Equal(0, renderer.Disposals);
        Assert.Equal(1, remote.Calls);
    }

    private sealed class QueuedInputRenderer : IUiRenderer, IUiInputSource
    {
        private readonly Channel<UiInput> _inputs = Channel.CreateUnbounded<UiInput>();

        public int Disposals { get; private set; }

        public void Enqueue(UiInput input)
            => Assert.True(_inputs.Writer.TryWrite(input));

        public ValueTask<UiInput> ReadAsync(CancellationToken cancellationToken)
            => _inputs.Reader.ReadAsync(cancellationToken);

        public ValueTask MaterializeAsync(UiPackage package, ImmutableArray<UiPropertyValue> values, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }
}
