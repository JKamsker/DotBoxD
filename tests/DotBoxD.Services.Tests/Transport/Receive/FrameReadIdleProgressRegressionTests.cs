using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class FrameReadIdleProgressRegressionTests
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ChunkGap = TimeSpan.FromMilliseconds(75);
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DisarmMargin = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task StreamConnection_ReceiveAsync_ResetsIdleTimeoutAcrossPendingPrefixAndBodyReads()
    {
        var expectedFrame = CreateFrame();
        await using var stream = new PendingByteStream();
        await using var connection = new StreamConnection(
            stream,
            ownsStream: false,
            frameReadIdleTimeout: IdleTimeout);
        var stopwatch = Stopwatch.StartNew();

        var receive = connection.ReceiveAsync();
        foreach (var value in expectedFrame)
        {
            Assert.False(receive.IsCompleted);
            await stream.CompleteNextReadAsync(value);
        }

        using var received = await receive.WaitAsync(Guard);
        stopwatch.Stop();

        Assert.Equal(expectedFrame, received.Memory.ToArray());
        Assert.True(stopwatch.Elapsed > IdleTimeout);

        var finalReadToken = stream.LastReadToken;
        Assert.True(finalReadToken.CanBeCanceled);
        await Task.Delay(IdleTimeout + DisarmMargin);
        Assert.False(finalReadToken.IsCancellationRequested);
    }

    [Fact]
    public async Task TcpConnection_ReceiveAsync_ResetsIdleTimeoutAcrossPendingPrefixAndBodyReads()
    {
        var expectedFrame = CreateFrame();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            using var client = new TcpClient { NoDelay = true };
            var accept = listener.AcceptTcpClientAsync();
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint).WaitAsync(Guard);
            var acceptedClient = await accept.WaitAsync(Guard);
            await using var connection = new TcpConnection(acceptedClient, IdleTimeout);
            var clientStream = client.GetStream();
            var stopwatch = Stopwatch.StartNew();

            var receive = connection.ReceiveAsync();
            foreach (var value in expectedFrame)
            {
                Assert.False(receive.IsCompleted);
                await Task.Delay(ChunkGap);
                await clientStream.WriteAsync(new byte[] { value }).AsTask().WaitAsync(Guard);
            }

            using var received = await receive.WaitAsync(Guard);
            stopwatch.Stop();

            Assert.Equal(expectedFrame, received.Memory.ToArray());
            Assert.True(stopwatch.Elapsed > IdleTimeout);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static byte[] CreateFrame()
    {
        using var frame = MessageFramer.FrameToPayload(
            messageId: 42,
            MessageType.Request,
            new byte[15]);
        return frame.Memory.ToArray();
    }

    private sealed class PendingByteStream : Stream
    {
        private readonly Channel<byte> _bytes = Channel.CreateUnbounded<byte>(
            new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = true,
            });
        private readonly SemaphoreSlim _readStarted = new(0);

        public CancellationToken LastReadToken { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public async Task CompleteNextReadAsync(byte value)
        {
            if (!await _readStarted.WaitAsync(Guard))
            {
                throw new TimeoutException("The connection did not start its next frame read.");
            }

            await Task.Delay(ChunkGap);
            await _bytes.Writer.WriteAsync(value);
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            LastReadToken = cancellationToken;
            _readStarted.Release();
            var value = await _bytes.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            buffer.Span[0] = value;
            return 1;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
