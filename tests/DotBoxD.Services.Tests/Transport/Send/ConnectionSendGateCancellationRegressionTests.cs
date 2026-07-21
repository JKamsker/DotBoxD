using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
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
    public async Task TcpConnection_LiveTokenCancelsWhileSendGateIsContended()
    {
        await using var pair = await ConnectedTcpPair.CreateAsync();
        var blockingFrame = CreateFrame(MessageFramer.MaxMessageSize);
        var smallFrame = CreateFrame(MessageFramer.HeaderSize);
        var firstSend = pair.Server.SendValueAsync(blockingFrame).AsTask();
        Assert.False(firstSend.IsCompleted);

        using var cancellation = new CancellationTokenSource();
        var waitingSend = pair.Server.SendValueAsync(smallFrame, cancellation.Token).AsTask();

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
            await pair.Server.DisposeAsync();
            await Record.ExceptionAsync(() => firstSend.WaitAsync(TestTimeout));
        }
    }

    private static byte[] CreateFrame(int length)
    {
        var frame = new byte[length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, length);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), 1);
        frame[8] = (byte)MessageType.Request;
        return frame;
    }

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
                var client = new TcpClient { ReceiveBufferSize = 1_024 };
                var acceptTask = listener.AcceptTcpClientAsync();
                await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
                var serverClient = await acceptTask.WaitAsync(TestTimeout);
                serverClient.SendBufferSize = 1_024;
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
