using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class ConnectionSendGateCancellationRegressionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StreamConnection_LiveTokenCancelsWhileSendGateIsContended()
    {
        await using var stream = new BlockingWriteStream();
        await using var connection = new StreamConnection(stream, ownsStream: false);
        var frame = CreateFrame(MessageFramer.HeaderSize);

        var firstSend = connection.SendValueAsync(frame).AsTask();
        await stream.WriteEntered.WaitAsync(TestTimeout);
        using var cancellation = new CancellationTokenSource();
        var waitingSend = connection.SendValueAsync(frame, cancellation.Token).AsTask();

        try
        {
            Assert.False(waitingSend.IsCompleted);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => waitingSend.WaitAsync(TestTimeout));
            Assert.False(firstSend.IsCompleted);
        }
        finally
        {
            stream.CompleteWrite();
            await firstSend.WaitAsync(TestTimeout);
        }
    }

    [Fact]
    public async Task StreamConnection_OwnedFrameCanceledWhileSendGateIsContendedIsDisposed()
    {
        await using var stream = new BlockingWriteStream();
        await using var connection = new StreamConnection(stream, ownsStream: false);
        var rawFrame = CreateFrame(MessageFramer.HeaderSize);
        var firstSend = connection.SendValueAsync(rawFrame).AsTask();
        await stream.WriteEntered.WaitAsync(TestTimeout);
        using var cancellation = new CancellationTokenSource();
        var ownedFrame = CreateOwnedFrame(MessageFramer.HeaderSize);
        var waitingSend = connection.SendFrameValueAsync(ownedFrame, cancellation.Token).AsTask();

        try
        {
            Assert.False(waitingSend.IsCompleted);
            _ = ownedFrame.WrittenMemory;
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => waitingSend.WaitAsync(TestTimeout));
            AssertDisposed(ownedFrame);
        }
        finally
        {
            stream.CompleteWrite();
            await firstSend.WaitAsync(TestTimeout);
        }
    }

    [Fact]
    public async Task TcpConnection_LiveTokenCancelsWhileSendGateIsContended()
    {
        await using var pair = await ConnectedTcpPair.CreateAsync();
        var smallFrame = CreateFrame(MessageFramer.HeaderSize);
        Assert.True(pair.Server.SendGate.Wait(0));

        try
        {
            using var cancellation = new CancellationTokenSource();
            var waitingSend = pair.Server.SendValueAsync(smallFrame, cancellation.Token).AsTask();
            var ownedFrame = CreateOwnedFrame(MessageFramer.HeaderSize);
            var ownedWaitingSend = pair.Server
                .SendFrameValueAsync(ownedFrame, cancellation.Token)
                .AsTask();
            Assert.False(waitingSend.IsCompleted);
            Assert.False(ownedWaitingSend.IsCompleted);
            _ = ownedFrame.WrittenMemory;
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => waitingSend.WaitAsync(TestTimeout));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => ownedWaitingSend.WaitAsync(TestTimeout));
            AssertDisposed(ownedFrame);
            Assert.Equal(0, pair.Server.SendGate.CurrentCount);
        }
        finally
        {
            pair.Server.ReleaseSendGate();
        }
    }

    [Fact]
    public async Task TcpConnection_DisposeCompletesAllSendsWaitingForGate()
    {
        await using var pair = await ConnectedTcpPair.CreateAsync();
        var smallFrame = CreateFrame(MessageFramer.HeaderSize);
        Assert.True(pair.Server.SendGate.Wait(0));

        try
        {
            var waitingSends = Enumerable.Range(0, 4)
                .Select(_ => pair.Server.SendValueAsync(smallFrame).AsTask())
                .ToArray();
            Assert.All(waitingSends, static waiting => Assert.False(waiting.IsCompleted));
            var ownedFrame = CreateOwnedFrame(MessageFramer.HeaderSize);
            var ownedWaitingSend = pair.Server.SendFrameValueAsync(ownedFrame).AsTask();
            Assert.False(ownedWaitingSend.IsCompleted);
            _ = ownedFrame.WrittenMemory;
            using var cancellation = new CancellationTokenSource();
            var canceledSend = pair.Server.SendValueAsync(smallFrame, cancellation.Token).AsTask();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => canceledSend.WaitAsync(TestTimeout));

            await pair.Server.DisposeAsync();
            foreach (var waitingSend in waitingSends)
            {
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    () => waitingSend.WaitAsync(TestTimeout));
            }

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => ownedWaitingSend.WaitAsync(TestTimeout));
            AssertDisposed(ownedFrame);
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => pair.Server.SendValueAsync(smallFrame).AsTask());
        }
        finally
        {
            pair.Server.ReleaseSendGate();
        }
    }

    [Fact]
    public void TransportSendGate_CancellationAfterFastAdmissionWinsOverTerminalPermit()
    {
        using var gate = new SemaphoreSlim(1, 1);
        using var cancellation = new CancellationTokenSource();
        var admission = TransportSendGate.WaitAsync(gate, cancellation.Token);
        Assert.True(admission.IsCompletedSuccessfully);

        var disposed = 1;
        TransportSendGate.WakeDisposedWaiters(gate);
        cancellation.Cancel();

        var exception = Record.Exception(() =>
        {
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
            }
            finally
            {
                TransportSendGate.ReleaseAfterSend(gate, ref disposed);
            }
        });

        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(1, gate.CurrentCount);
    }

    private static byte[] CreateFrame(int length)
    {
        var frame = new byte[length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, length);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), 1);
        frame[8] = (byte)MessageType.Request;
        return frame;
    }

    private static PooledBufferWriter CreateOwnedFrame(int length)
    {
        var frame = new PooledBufferWriter(length);
        var span = frame.GetSpan(length);
        span.Slice(0, length).Clear();
        BinaryPrimitives.WriteInt32LittleEndian(span, length);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), 1);
        span[8] = (byte)MessageType.Request;
        frame.Advance(length);
        return frame;
    }

    private static void AssertDisposed(PooledBufferWriter frame) =>
        Assert.Throws<ObjectDisposedException>(() => _ = frame.WrittenMemory);

    private sealed class BlockingWriteStream : Stream
    {
        private readonly TaskCompletionSource _writeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _writeReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WriteEntered => _writeEntered.Task;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void CompleteWrite() => _writeReleased.TrySetResult();

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _writeEntered.TrySetResult();
            return new ValueTask(_writeReleased.Task.WaitAsync(cancellationToken));
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ConnectedTcpPair : IAsyncDisposable
    {
        private readonly TcpClient _client;

        private ConnectedTcpPair(TcpConnection server, TcpClient client)
        {
            Server = server;
            _client = client;
        }

        public TcpConnection Server { get; }

        public static async Task<ConnectedTcpPair> CreateAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                var client = new TcpClient();
                var acceptTask = listener.AcceptTcpClientAsync();
                await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
                var serverClient = await acceptTask.WaitAsync(TestTimeout);
                return new ConnectedTcpPair(new TcpConnection(serverClient), client);
            }
            finally
            {
                listener.Stop();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Server.DisposeAsync();
            _client.Dispose();
        }
    }
}
