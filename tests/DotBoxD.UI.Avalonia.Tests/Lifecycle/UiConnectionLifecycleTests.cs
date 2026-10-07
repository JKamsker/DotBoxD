using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Testing;
using DotBoxD.UI.Authoring;
using DotBoxD.UI.Runtime;
using Examples.SandboxedUi.Host;

namespace DotBoxD.UI.Avalonia.Tests.Lifecycle;

public sealed class UiConnectionLifecycleTests
{
    [AvaloniaTheory]
    [InlineData("Before")]
    [InlineData("After")]
    [InlineData("Host")]
    public async Task Connection_close_and_host_shutdown_release_idle_sessions(string close)
    {
        var (channel, remote) = InMemoryRpcChannel.CreatePair();
        await using var remoteOwner = remote;
        await using var peer = RpcPeer.Over(channel, new MessagePackRpcSerializer());
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.Disconnected += (_, _) => disconnected.TrySetResult();
        peer.Start();
        var b = new UiBuilder();
        var text = b.State("");
        var renderer = new AvaloniaUiRenderer();
        using var sandbox = SandboxHost.Create(h => h.AddDefaultPureBindings());
        await using var session = await new UiHost(sandbox, SandboxPolicyBuilder.Create().Build())
            .InstallAsync(b.Build(b.Stack(b.TextBox(text))), renderer);
        var panel = Assert.IsType<StackPanel>(renderer.Root);
        var box = Assert.IsType<TextBox>(panel.Children[0]);
        if (close == "Before")
        {
            await remote.DisposeAsync();
            await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        await using var connection = new UiSessionConnection(peer, session);
        if (close == "After")
        { await remote.DisposeAsync(); }
        if (close == "Host")
        {
            await connection.DisposeAsync();
            Assert.True(peer.IsConnected);
        }

        await connection.Released.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(session.IsDisconnected);
        Assert.True(renderer.IsDisposed);
        Assert.Null(renderer.Root);
        Assert.Empty(panel.Children);
        box.Text = "detached";
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.SnapshotAsync().AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.SetInputAsync(1, UiPropertyId.Text, UiValue.FromString("late")).AsTask());
    }

    [Fact]
    public async Task Connection_owner_observes_async_session_cleanup_failures()
    {
        var (channel, remote) = InMemoryRpcChannel.CreatePair();
        await using var remoteOwner = remote;
        await using var peer = RpcPeer.Over(channel, new MessagePackRpcSerializer());
        var b = new UiBuilder();
        var renderer = new FailingDisposeRenderer();
        using var sandbox = SandboxHost.Create(h => h.AddDefaultPureBindings());
        var session = await new UiHost(sandbox, SandboxPolicyBuilder.Create().Build()).InstallAsync(b.Build(b.Text("Idle")), renderer);
        var connection = new UiSessionConnection(peer, session);
        await Assert.ThrowsAsync<IOException>(() => connection.DisposeAsync().AsTask());
        await Assert.ThrowsAsync<IOException>(() => connection.Released);
        Assert.True(session.IsDisconnected);
        Assert.Equal(1, renderer.Disposals);
    }

    private sealed class FailingDisposeRenderer : IUiRenderer
    {
        public int Disposals { get; private set; }
        public ValueTask MaterializeAsync(UiPackage package, ImmutableArray<UiPropertyValue> values, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
        public ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.FromException(new IOException("cleanup failure"));
        }
    }
}
