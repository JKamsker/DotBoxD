using DotBoxD.Plugins;
using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Attributes;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Testing;
using DotBoxD.Services.Transport;

namespace DotBoxD.Kernels.Tests.Plugins;

// Regression: PluginConnectionHost is the untrusted plugin boundary, so its default (options == null) must
// not inherit RpcMessagePackIpc's trusted-client server profile (unbounded inbound queue, which starts a
// handler for every request immediately). A plugin flooding requests must be capped at a bounded number of
// concurrently running handlers, with the rest held back by the bounded inbound queue / read backpressure.
public sealed class PluginConnectionHostInboundBoundTests
{
    private const int ExpectedConcurrentHandlers = 64;
    private const int FloodedCalls = ExpectedConcurrentHandlers * 3;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Default_options_bound_concurrent_inbound_handlers_from_a_flooding_plugin()
    {
        var (serverChannel, clientChannel) = InMemoryRpcChannel.CreatePair();
        using var server = PluginServer.Create();
        var probe = new BlockingProbe();

        await using var host = await PluginConnectionHost<object>.StartAsync(
            server,
            new SingleConnectionServerTransport(serverChannel, ownsConnection: true),
            (peer, _) =>
            {
                peer.Provide<IPluginBoundaryFloodProbe>(probe);
                return new object();
            });

        await using var session = await RpcMessagePackIpc.ConnectAsync(
            new SingleConnectionTransport(clientChannel, ownsConnection: true),
            new RpcPeerOptions { RequestTimeout = Timeout });
        var proxy = session.Get<IPluginBoundaryFloodProbe>();

        var calls = new Task<int>[FloodedCalls];
        for (var i = 0; i < calls.Length; i++)
        {
            calls[i] = proxy.WaitAsync(i).AsTask();
        }

        int enteredWhileBlocked;
        try
        {
            // Concurrent (not serial) dispatch: the cap is reached ...
            await probe.WaitForEnteredAsync(ExpectedConcurrentHandlers, Timeout);

            // ... and not exceeded while every handler is blocked.
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            enteredWhileBlocked = probe.Entered;
        }
        finally
        {
            // Always unblock handlers: the profile ignores inbound cancellation, so host disposal waits for them.
            probe.Release();
        }

        Assert.Equal(ExpectedConcurrentHandlers, enteredWhileBlocked);
        var results = await Task.WhenAll(calls).WaitAsync(Timeout);
        Assert.Equal(FloodedCalls, results.Length);
        Assert.Equal(FloodedCalls, probe.Entered);
    }

    private sealed class BlockingProbe : IPluginBoundaryFloodProbe
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _entered;

        public int Entered => Volatile.Read(ref _entered);

        public async ValueTask<int> WaitAsync(int value, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _entered);
            await _release.Task.ConfigureAwait(false);
            return value;
        }

        public void Release() => _release.TrySetResult();

        public async Task WaitForEnteredAsync(int count, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            while (Entered < count)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), cts.Token).ConfigureAwait(false);
            }
        }
    }
}

[RpcService]
public interface IPluginBoundaryFloodProbe
{
    ValueTask<int> WaitAsync(int value, CancellationToken cancellationToken = default);
}
