using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class TransportConnectionConstructionProbe
{
    private const int Iterations = 10_000;
    private const int TcpDisposalIterations = 256;

    public static async Task RunAsync()
    {
        var namedPipe = await MeasureNamedPipeAsync().ConfigureAwait(false);
        var tcp = await MeasureTcpConstructionAsync().ConfigureAwait(false);
        var tcpDisposalBytes = await MeasureLiveTcpDisposalAllocatedBytesAsync().ConfigureAwait(false);
        tcp = tcp with
        {
            DisposalBytesPerConnection = tcpDisposalBytes / (double)TcpDisposalIterations,
        };

        Console.WriteLine("Transport connection construction probe");
        Console.WriteLine("transport          ns/connection   B/connection   idle-dispose B/op");
        Write(namedPipe);
        Write(tcp);
        Console.WriteLine(
            $"invariants: {Iterations:N0} wrappers over one live OS connection/lane; " +
            $"{TcpDisposalIterations:N0} distinct live TCP connections disposed");
    }

    private static async Task<Measurement> MeasureNamedPipeAsync()
    {
        var pipeName = $"dotboxd-construction-{Guid.NewGuid():N}";
        using var receiver = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        using var sender = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        var accepting = receiver.WaitForConnectionAsync();
        await sender.ConnectAsync().ConfigureAwait(false);
        await accepting.ConfigureAwait(false);

        var warmupConnection = new StreamConnection(
            receiver,
            ownsStream: true,
            frameReadIdleTimeout: Timeout.InfiniteTimeSpan);
        var connections = new StreamConnection[Iterations];
        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startedAt = Stopwatch.GetTimestamp();
        for (var index = 0; index < connections.Length; index++)
        {
            connections[index] = new StreamConnection(
                receiver,
                ownsStream: true,
                frameReadIdleTimeout: Timeout.InfiniteTimeSpan);
        }

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        await warmupConnection.DisposeAsync().ConfigureAwait(false);
        GC.KeepAlive(connections);
        return new Measurement("named pipe", elapsed, allocated, DisposalBytesPerConnection: null);
    }

    private static async Task<Measurement> MeasureTcpConstructionAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        using var sender = new TcpClient(AddressFamily.InterNetwork);
        var accepting = listener.AcceptTcpClientAsync();
        await sender.ConnectAsync(endpoint.Address, endpoint.Port).ConfigureAwait(false);
        using var receiver = await accepting.ConfigureAwait(false);

        var warmupConnection = new TcpConnection(receiver, Timeout.InfiniteTimeSpan);
        var connections = new TcpConnection[Iterations];
        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startedAt = Stopwatch.GetTimestamp();
        for (var index = 0; index < connections.Length; index++)
        {
            connections[index] = new TcpConnection(receiver, Timeout.InfiniteTimeSpan);
        }

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        await warmupConnection.DisposeAsync().ConfigureAwait(false);
        foreach (var connection in connections)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        GC.KeepAlive(connections);
        return new Measurement("TCP", elapsed, allocated, DisposalBytesPerConnection: null);
    }

    private static async Task<long> MeasureLiveTcpDisposalAllocatedBytesAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var peers = new TcpClient[TcpDisposalIterations];
        var connections = new TcpConnection[TcpDisposalIterations];
        for (var index = 0; index < connections.Length; index++)
        {
            var peer = new TcpClient(AddressFamily.InterNetwork);
            peers[index] = peer;
            var accepting = listener.AcceptTcpClientAsync();
            await peer.ConnectAsync(endpoint.Address, endpoint.Port).ConfigureAwait(false);
            var server = await accepting.ConfigureAwait(false);
            connections[index] = new TcpConnection(server, Timeout.InfiniteTimeSpan);
        }

        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        foreach (var connection in connections)
        {
            connection.DisposeAsync().GetAwaiter().GetResult();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        foreach (var peer in peers)
        {
            peer.Dispose();
        }

        GC.KeepAlive(connections);
        return allocated;
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Write(Measurement measurement)
    {
        Console.WriteLine(
            $"{measurement.Name,-18} {measurement.NanosecondsPerConnection,13:N1} " +
            $"{measurement.BytesPerConnection,14:N1} {measurement.FormattedDisposalBytes,19}");
    }

    private readonly record struct Measurement(
        string Name,
        TimeSpan Elapsed,
        long AllocatedBytes,
        double? DisposalBytesPerConnection)
    {
        public double NanosecondsPerConnection => Elapsed.TotalNanoseconds / Iterations;

        public double BytesPerConnection => AllocatedBytes / (double)Iterations;

        public string FormattedDisposalBytes => DisposalBytesPerConnection is null
            ? "n/a"
            : DisposalBytesPerConnection.Value.ToString("N1");
    }
}
