using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;

namespace DotBoxD.Kernels.Benchmarks.Ipc;

using System.Globalization;
using System.Net;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;

internal static class IpcAllocationProfile
{
    public const string NamedPipeTransport = "namedpipe";
    public const string InMemoryTransport = "inmemory";
    public const string TcpTransportName = "tcp";

    public static async Task RunAsync(
        string transport,
        int iterations,
        bool disableTimeout,
        bool finiteTimeout,
        bool lowAllocationProfile,
        bool taskBackedClient)
    {
        if (iterations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "Iterations must be positive.");
        }

        if (disableTimeout && finiteTimeout)
        {
            throw new ArgumentException("IPC profile timeout modes are mutually exclusive.");
        }

        if (taskBackedClient && !lowAllocationProfile)
        {
            throw new ArgumentException("The task-backed client control requires the low-allocation profile.");
        }

        await using var fixture = await CreateFixtureAsync(
            transport,
            disableTimeout,
            finiteTimeout,
            lowAllocationProfile,
            taskBackedClient).ConfigureAwait(false);
        var service = fixture.Session.Get<IAllocationProbeService>();

        await service.AddAsync(1).ConfigureAwait(false);
        await service.EchoAsync(new PingRequest(1, 1)).ConfigureAwait(false);

        var intBytes = await MeasureAddAllocationsAsync(service, iterations).ConfigureAwait(false);
        var structBytes = await MeasureEchoAllocationsAsync(service, iterations).ConfigureAwait(false);

        Console.WriteLine("IPC profile transport: " + transport);
        Console.WriteLine(
            "IPC profile timeout: " +
            (finiteTimeout ? "finite" : disableTimeout || lowAllocationProfile ? "disabled" : "default"));
        Console.WriteLine("IPC profile low allocation: " + (lowAllocationProfile ? "enabled" : "disabled"));
        Console.WriteLine(
            "IPC profile pooled client: " +
            (lowAllocationProfile && !taskBackedClient ? "enabled" : "disabled"));
        Console.WriteLine("IPC profile iterations: " + iterations.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("AddAsync total allocated bytes: " + intBytes.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("AddAsync allocated bytes/call: " + FormatBytesPerCall(intBytes, iterations));
        Console.WriteLine("EchoAsync total allocated bytes: " + structBytes.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("EchoAsync allocated bytes/call: " + FormatBytesPerCall(structBytes, iterations));
    }

    private static async Task<ProfileFixture> CreateFixtureAsync(
        string transport,
        bool disableTimeout,
        bool finiteTimeout,
        bool lowAllocationProfile,
        bool taskBackedClient)
    {
        var clientOptions = CreateClientOptions(
            disableTimeout,
            finiteTimeout,
            lowAllocationProfile,
            taskBackedClient);
        var serverOptions = CreateServerOptions(disableTimeout, lowAllocationProfile);
        if (transport.Equals(NamedPipeTransport, StringComparison.OrdinalIgnoreCase))
        {
            var pipeName = "dotboxd-ipc-profile-" + Guid.NewGuid().ToString("N");
            var host = RpcMessagePackIpc.ListenNamedPipe(
                pipeName,
                peer => peer.Provide<IAllocationProbeService>(new AllocationProbeService()),
                serverOptions);
            await host.StartAsync().ConfigureAwait(false);
            var session = await RpcMessagePackIpc.ConnectNamedPipeAsync(pipeName, clientOptions)
                .ConfigureAwait(false);
            return new ProfileFixture(host, session);
        }

        if (transport.Equals(InMemoryTransport, StringComparison.OrdinalIgnoreCase))
        {
            var (serverChannel, clientChannel) = InMemoryRpcChannel.CreatePair();
            var host = RpcMessagePackIpc.Listen(
                new SingleConnectionServerTransport(serverChannel, ownsConnection: true),
                peer => peer.Provide<IAllocationProbeService>(new AllocationProbeService()),
                serverOptions ?? new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) });
            await host.StartAsync().ConfigureAwait(false);
            var session = await RpcMessagePackIpc.ConnectAsync(
                    new SingleConnectionTransport(clientChannel, ownsConnection: true),
                    clientOptions)
                .ConfigureAwait(false);
            return new ProfileFixture(host, session);
        }

        if (transport.Equals(TcpTransportName, StringComparison.OrdinalIgnoreCase))
        {
            var frameReadIdleTimeout = disableTimeout
                ? Timeout.InfiniteTimeSpan
                : (TimeSpan?)null;
            var serverTransport = new TcpServerTransport(IPAddress.Loopback, 0)
            {
                FrameReadIdleTimeout = frameReadIdleTimeout,
            };
            var host = RpcMessagePackIpc.Listen(
                serverTransport,
                peer => peer.Provide<IAllocationProbeService>(new AllocationProbeService()),
                serverOptions);
            try
            {
                await host.StartAsync().ConfigureAwait(false);
                var endpoint = serverTransport.LocalEndpoint ??
                    throw new InvalidOperationException(
                        "TCP profile server did not expose a bound endpoint.");
                var clientTransport = new TcpTransport(endpoint.Address.ToString(), endpoint.Port)
                {
                    FrameReadIdleTimeout = frameReadIdleTimeout,
                };
                var session = await RpcMessagePackIpc.ConnectAsync(clientTransport, clientOptions)
                    .ConfigureAwait(false);
                return new ProfileFixture(host, session);
            }
            catch
            {
                await host.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        throw new ArgumentException($"Unknown IPC profile transport '{transport}'.", nameof(transport));
    }

    private static RpcPeerOptions? CreateClientOptions(
        bool disableTimeout,
        bool finiteTimeout,
        bool lowAllocationProfile,
        bool taskBackedClient)
    {
        if (!disableTimeout && !finiteTimeout && !lowAllocationProfile)
        {
            return null;
        }

        return new RpcPeerOptions
        {
            EnableLowAllocationValueTaskInvocations = lowAllocationProfile && !taskBackedClient,
            RejectInboundCalls = true,
            RequestTimeout = disableTimeout || lowAllocationProfile && !finiteTimeout
                ? Timeout.InfiniteTimeSpan
                : TimeSpan.FromSeconds(30)
        };
    }

    private static RpcPeerOptions? CreateServerOptions(bool disableTimeout, bool lowAllocationProfile)
    {
        if (lowAllocationProfile)
        {
            return new RpcPeerOptions
            {
                DisableInboundRequestCancellation = true,
                InboundQueueCapacity = null,
                RequestTimeout = Timeout.InfiniteTimeSpan,
            };
        }

        return disableTimeout
            ? new RpcPeerOptions { RequestTimeout = Timeout.InfiniteTimeSpan }
            : null;
    }

    private static string FormatBytesPerCall(long allocatedBytes, int iterations)
        => (allocatedBytes / (double)iterations).ToString("N1", CultureInfo.InvariantCulture);

    private static async Task<long> MeasureAddAllocationsAsync(IAllocationProbeService service, int iterations)
    {
        Collect();

        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < iterations; i++)
        {
            _ = await service.AddAsync(42).ConfigureAwait(false);
        }

        var after = GC.GetTotalAllocatedBytes(precise: true);
        return after - before;
    }

    private static async Task<long> MeasureEchoAllocationsAsync(IAllocationProbeService service, int iterations)
    {
        Collect();

        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < iterations; i++)
        {
            _ = await service.EchoAsync(new PingRequest(42, 123)).ConfigureAwait(false);
        }

        var after = GC.GetTotalAllocatedBytes(precise: true);
        return after - before;
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class ProfileFixture : IAsyncDisposable
    {
        private readonly RpcHost _host;

        public ProfileFixture(RpcHost host, RpcPeerSession session)
        {
            _host = host;
            Session = session;
        }

        public RpcPeerSession Session { get; }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Session.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await _host.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
