using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotBoxD.Plugins;
using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Testing;
using DotBoxD.Services.Transport;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginConnectionHostLifetimeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Shutdown_releases_server_session_and_configuration(bool connect, bool dispose)
    {
        var (host, references) = await CreateStoppedAsync(connect, dispose);
        await using (host)
        {
            await AssertCollectedAsync(references);
            GC.KeepAlive(host);
        }
    }

    [Fact]
    public async Task Connected_host_releases_used_configuration()
    {
        using var server = PluginServer.Create();
        var (host, remote, reference) = await CreateConnectedAsync(server);
        await using var remoteOwner = remote;
        await using (host)
        {
            await AssertCollectedAsync([reference]);
            Assert.False(host.Disconnected.IsCompleted);
            GC.KeepAlive(host);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(PluginConnectionHost<object> Host, WeakReference[] References)> CreateStoppedAsync(
        bool connect, bool dispose)
    {
        using var server = PluginServer.Create();
        var owner = new ConfigurationOwner();
        var (channel, remote) = InMemoryRpcChannel.CreatePair();
        await using var remoteOwner = remote;
        IServerTransport transport = connect
            ? new SingleConnectionServerTransport(channel, ownsConnection: true)
            : new IdleTransport();
        await using var channelOwner = channel;
        var host = await PluginConnectionHost<object>.StartAsync(server, transport, owner.Configure);
        if (connect)
        {
            await host.Connected.WaitAsync(TimeSpan.FromSeconds(5));
        }
        var references = new List<WeakReference> { new(server), new(owner) };
        if (owner.Session is { } session)
        {
            references.Add(new WeakReference(session));
        }
        if (dispose)
        {
            await host.DisposeAsync();
        }
        else
        {
            await host.StopAsync();
        }
        await host.Disconnected.WaitAsync(TimeSpan.FromSeconds(5));
        using var independentSession = server.CreateSession();
        return (host, references.ToArray());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(PluginConnectionHost<object> Host, IRpcChannel Remote, WeakReference Reference)> CreateConnectedAsync(
        PluginServer server)
    {
        var owner = new ConfigurationOwner();
        var (channel, remote) = InMemoryRpcChannel.CreatePair();
        var host = await PluginConnectionHost<object>.StartAsync(server,
            new SingleConnectionServerTransport(channel, ownsConnection: true), owner.Configure);
        // The channel pair stays alive until host disposal; no client session is needed for configuration.
        await host.Connected.WaitAsync(TimeSpan.FromSeconds(5));
        return (host, remote, new WeakReference(owner));
    }

    private static async Task AssertCollectedAsync(WeakReference[] references)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (references.All(reference => !reference.IsAlive))
            {
                return;
            }
            await Task.Delay(20);
        }
        while (timer.Elapsed < TimeSpan.FromSeconds(5));
        Assert.All(references, reference => Assert.False(reference.IsAlive));
    }

    private sealed class ConfigurationOwner
    {
        public PluginSession? Session { get; private set; }

        public object Configure(RpcPeer peer, PluginSession session)
        {
            Session = session;
            return new object();
        }
    }

    private sealed class IdleTransport : IServerTransport
    {
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("The idle transport cannot accept connections.");
        }
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
