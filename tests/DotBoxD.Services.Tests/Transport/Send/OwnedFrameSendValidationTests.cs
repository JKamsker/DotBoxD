using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class OwnedFrameSendValidationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Tcp_owned_frame_rejects_null_frame()
    {
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => TcpConnectionFrameSender.SendAsync(
                connection: null!,
                frame: null!,
                CancellationToken.None).AsTask());

        Assert.Equal("frame", ex.ParamName);
    }

    [Fact]
    public async Task Stream_owned_frame_honors_configured_maximum_and_is_disposed()
    {
        using var stream = new MemoryStream();
        await using var connection = new StreamConnection(
            stream,
            ownsStream: false,
            maxMessageSize: MessageFramer.HeaderSize + 1);
        var frame = new PooledBufferWriter();
        MessageFramer.WriteFrame(frame, 1, MessageType.Request, new byte[10]);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => connection.SendFrameValueAsync(frame).AsTask());

        AssertDisposed(frame);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public async Task Validated_streaming_path_defers_configured_maximum_to_stream_transport()
    {
        using var stream = new MemoryStream();
        await using var connection = new StreamConnection(
            stream,
            ownsStream: false,
            maxMessageSize: MessageFramer.HeaderSize + 1);
        using var peerSender = new RpcPeerSender(connection, static () => false);
        var validatedSender = Assert.IsType<ValidatedOwnedFrameSender>(
            peerSender.ValidatedFrameSender);
        var streamSender = new RpcStreamFrameSender(peerSender.SendAsync, validatedSender);
        var frame = new PooledBufferWriter();
        MessageFramer.WriteFrame(frame, 1, MessageType.StreamItem, new byte[2]);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => streamSender.SendAsync(frame, CancellationToken.None).AsTask());

        AssertDisposed(frame);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public async Task Stream_disposed_connection_wins_over_pre_cancellation_and_disposes_frame()
    {
        await using var connection = new StreamConnection(new MemoryStream(), ownsStream: false);
        await connection.DisposeAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var frame = new PooledBufferWriter(MessageFramer.HeaderSize);
        MessageFramer.WriteFrame(frame, 1, MessageType.Request, ReadOnlySpan<byte>.Empty);

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => connection.SendFrameValueAsync(frame, cancellation.Token).AsTask());

        AssertDisposed(frame);
    }

    [Fact]
    public async Task Tcp_owned_frame_rejects_mismatched_length_without_writing_and_is_disposed()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync();
        var port = server.LocalEndpoint?.Port ?? throw new InvalidOperationException("No bound port.");
        using var client = new TcpClient();
        var accept = server.AcceptAsync();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var accepted = await accept.WaitAsync(Timeout);
        var frameChannel = Assert.IsAssignableFrom<IRpcFrameChannel>(accepted);
        var frame = CreateMismatchedFrame();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => frameChannel.SendFrameValueAsync(frame).AsTask());

        AssertDisposed(frame);
        using var noData = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetStream().ReadAsync(new byte[1], noData.Token).AsTask());
    }

    [Fact]
    public async Task Validated_streaming_path_defers_malformed_frame_to_tcp_transport()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync();
        var port = server.LocalEndpoint?.Port ?? throw new InvalidOperationException("No bound port.");
        using var client = new TcpClient();
        var accept = server.AcceptAsync();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var accepted = await accept.WaitAsync(Timeout);
        using var peerSender = new RpcPeerSender(accepted, static () => false);
        var validatedSender = Assert.IsType<ValidatedOwnedFrameSender>(
            peerSender.ValidatedFrameSender);
        var streamSender = new RpcStreamFrameSender(peerSender.SendAsync, validatedSender);
        var frame = CreateMismatchedFrame();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => streamSender.SendAsync(frame, CancellationToken.None).AsTask());

        AssertDisposed(frame);
        using var noData = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetStream().ReadAsync(new byte[1], noData.Token).AsTask());
    }

    private static PooledBufferWriter CreateMismatchedFrame()
    {
        var frame = new PooledBufferWriter(MessageFramer.HeaderSize);
        var span = frame.GetSpan(MessageFramer.HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(span, MessageFramer.HeaderSize + 1);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), 1);
        span[8] = (byte)MessageType.Request;
        frame.Advance(MessageFramer.HeaderSize);
        return frame;
    }

    private static void AssertDisposed(PooledBufferWriter frame) =>
        Assert.Throws<ObjectDisposedException>(() => _ = frame.WrittenMemory);
}
