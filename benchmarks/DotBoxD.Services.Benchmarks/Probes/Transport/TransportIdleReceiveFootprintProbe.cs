using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class TransportIdleReceiveFootprintProbe
{
    private const int ConnectionCount = 1_000;

    private static readonly FieldInfo StreamReceiveBufferField = GetReceiveBufferField(
        typeof(StreamConnection),
        "_receiveBuffer");

    private static readonly FieldInfo TcpReceiveBufferField = GetReceiveBufferField(
        typeof(TcpConnection),
        "_receiveBuffer");

    public static async Task RunAsync(bool taskBacked = true)
    {
        var namedPipe = await MeasureNamedPipesAsync(taskBacked).ConfigureAwait(false);
        var tcp = await MeasureTcpAsync(taskBacked).ConfigureAwait(false);

        Console.WriteLine("Idle pending transport receive footprint probe");
        Console.WriteLine("completion: " + (taskBacked ? "AsTask" : "direct ValueTask"));
        Console.WriteLine(
            "transport       connections  pending  rented windows  logical KiB  allocated B  live delta B");
        Write(namedPipe);
        Write(tcp);
        Console.WriteLine(
            $"invariants: {ConnectionCount:N0} real OS connection pairs/lane, no peer bytes written, " +
            $"{StreamFrameReceiveBuffer.LookaheadCapacity / 1_024:N0} KiB per rented window");
    }

    private static async Task<Measurement> MeasureNamedPipesAsync(bool taskBacked)
    {
        var connections = new List<StreamConnection>(ConnectionCount);
        var senders = new List<NamedPipeClientStream>(ConnectionCount);
        var receives = new PendingFrameReceiveSet(ConnectionCount, taskBacked);
        try
        {
            for (var index = 0; index < ConnectionCount; index++)
            {
                var pipeName = $"dotboxd-idle-footprint-{Guid.NewGuid():N}";
                var receiver = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                var sender = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);
                var accepting = receiver.WaitForConnectionAsync();
                await sender.ConnectAsync().ConfigureAwait(false);
                await accepting.ConfigureAwait(false);
                senders.Add(sender);
                connections.Add(new StreamConnection(
                    receiver,
                    ownsStream: true,
                    frameReadIdleTimeout: Timeout.InfiniteTimeSpan));
            }

            ForceGc();
            var liveBefore = GC.GetTotalMemory(forceFullCollection: false);
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            foreach (var connection in connections)
            {
                receives.Add(connection.ReceiveFrameValueAsync());
            }

            var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            var liveAfter = GC.GetTotalMemory(forceFullCollection: true);
            var rented = connections.Count(HasRentedBuffer);
            return new Measurement("named pipe", receives.Count, rented, allocated, liveAfter - liveBefore);
        }
        finally
        {
            await DisposeConnectionsAsync(connections).ConfigureAwait(false);
            foreach (var sender in senders)
            {
                sender.Dispose();
            }

            await receives.ObserveAsync().ConfigureAwait(false);
        }
    }

    private static async Task<Measurement> MeasureTcpAsync(bool taskBacked)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start(ConnectionCount);
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var connections = new List<TcpConnection>(ConnectionCount);
        var senders = new List<TcpClient>(ConnectionCount);
        var receives = new PendingFrameReceiveSet(ConnectionCount, taskBacked);
        try
        {
            for (var index = 0; index < ConnectionCount; index++)
            {
                var sender = new TcpClient(AddressFamily.InterNetwork);
                var accepting = listener.AcceptTcpClientAsync();
                await sender.ConnectAsync(endpoint.Address, endpoint.Port).ConfigureAwait(false);
                var receiver = await accepting.ConfigureAwait(false);
                senders.Add(sender);
                connections.Add(new TcpConnection(receiver, Timeout.InfiniteTimeSpan));
            }

            ForceGc();
            var liveBefore = GC.GetTotalMemory(forceFullCollection: false);
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            foreach (var connection in connections)
            {
                receives.Add(connection.ReceiveFrameValueAsync());
            }

            var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            var liveAfter = GC.GetTotalMemory(forceFullCollection: true);
            var rented = connections.Count(HasRentedBuffer);
            return new Measurement("TCP", receives.Count, rented, allocated, liveAfter - liveBefore);
        }
        finally
        {
            foreach (var connection in connections)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            foreach (var sender in senders)
            {
                sender.Dispose();
            }

            await receives.ObserveAsync().ConfigureAwait(false);
        }
    }

    private static bool HasRentedBuffer(StreamConnection connection) =>
        GetReceiveBuffer(StreamReceiveBufferField, connection).HasBuffer;

    private static bool HasRentedBuffer(TcpConnection connection) =>
        GetReceiveBuffer(TcpReceiveBufferField, connection).HasBuffer;

    private static StreamFrameReceiveBuffer GetReceiveBuffer(FieldInfo field, object connection) =>
        (StreamFrameReceiveBuffer)(field.GetValue(connection)
            ?? throw new InvalidOperationException($"{field.DeclaringType?.Name} receive buffer is null."));

    private static FieldInfo GetReceiveBufferField(Type connectionType, string name) =>
        connectionType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{connectionType.Name}.{name} was not found.");

    private static async Task DisposeConnectionsAsync(IEnumerable<StreamConnection> connections)
    {
        foreach (var connection in connections)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
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
            $"{measurement.Name,-15} {ConnectionCount,11:N0} {measurement.PendingReceives,8:N0} " +
            $"{measurement.RentedWindows,15:N0} {measurement.LogicalKilobytes,12:N0} " +
            $"{measurement.AllocatedBytes,12:N0} {measurement.LiveBytesDelta,13:N0}");
    }

    private readonly record struct Measurement(
        string Name,
        int PendingReceives,
        int RentedWindows,
        long AllocatedBytes,
        long LiveBytesDelta)
    {
        public long LogicalKilobytes =>
            RentedWindows * (StreamFrameReceiveBuffer.LookaheadCapacity / 1_024L);
    }
}
