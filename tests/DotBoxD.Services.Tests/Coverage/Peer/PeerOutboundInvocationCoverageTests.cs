using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Peer;

/// <summary>
/// Behavioral coverage for the outbound (client) half of <see cref="RpcPeer"/>: request framing,
/// response correlation, remote-error surfacing, timeouts, cancellation (cancel frames), send
/// failures, and the disposal path that faults pending requests. These drive the internal
/// <c>RpcPeerOutboundInvoker</c>, <c>PendingRequests</c>, <c>ReceivedResponse</c>,
/// <c>RpcPeerSender</c>, and <c>RpcPeerCancelFrameSender</c> purely through the public
/// <see cref="RpcPeer"/> API plus frame injection over <see cref="IRpcChannel"/>.
/// </summary>
public sealed class PeerOutboundInvocationCoverageTests
{
    private const string Service = "Svc";
    private const string Method = "Op";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static MessagePackRpcSerializer NewSerializer() => new();

    /// <summary>Polls <paramref name="condition"/> with a bounded deadline so a regression fails fast
    /// instead of hanging, without a fixed sleep used for synchronization.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not satisfied within the timeout.");
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    private static RpcPeerOptions Options(TimeSpan? requestTimeout = null) =>
        new() { RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(5) };

    /// <summary>
    /// Frames a Response (or Error) frame the read loop will correlate to an outbound request by id.
    /// </summary>
    private static Payload ResponseFrame<TResult>(
        ISerializer serializer,
        int messageId,
        TResult result,
        bool isSuccess = true,
        MessageType type = MessageType.Response)
    {
        var response = new RpcResponse
        {
            MessageId = messageId,
            IsSuccess = isSuccess,
        };

        var payloadWriter = new ArrayBufferWriter<byte>();
        serializer.Serialize(payloadWriter, result);
        return MessageFramer.FrameMessage(serializer, messageId, type, response, payloadWriter.WrittenSpan);
    }

    private static Payload ErrorFrame(
        ISerializer serializer,
        int messageId,
        string errorMessage,
        string errorType)
    {
        var response = new RpcResponse
        {
            MessageId = messageId,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ErrorType = errorType,
        };

        return MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Error,
            response,
            ReadOnlySpan<byte>.Empty);
    }

    [Fact]
    public async Task InvokeAsync_WithMatchingResponseFrame_ReturnsDeserializedResult()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        // First outbound call on a fresh peer always uses message id 1 (counter is Interlocked.Increment from 0).
        var call = peer.InvokeAsync<int, string>(Service, Method, request: 7);
        channel.Enqueue(ResponseFrame(serializer, messageId: 1, result: "pong"));

        var result = await call.WaitAsync(Timeout);

        Assert.Equal("pong", result);
    }

    [Fact]
    public async Task InvokeAsync_NoRequestNoResponseBody_CompletesWhenResponseFrameArrives()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        // Parameterless, void-returning overload: exercises the no-argument SendRequestAsync +
        // FrameMessage(empty) path and the discard-result Invoke overload.
        var call = peer.InvokeAsync(Service, Method);
        channel.Enqueue(MessageFramer.FrameMessage(
            serializer,
            messageId: 1,
            MessageType.Response,
            new RpcResponse { MessageId = 1, IsSuccess = true },
            ReadOnlySpan<byte>.Empty));

        await call.WaitAsync(Timeout);
    }

    [Fact]
    public async Task InvokeAsync_RequestNoResponseBody_CompletesWhenResponseFrameArrives()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        // Request payload but void return: exercises the with-argument FrameRequest path and the
        // discard-result Invoke overload (using var _ = ...).
        var call = peer.InvokeAsync<string>(Service, Method, request: "hello");
        channel.Enqueue(MessageFramer.FrameMessage(
            serializer,
            messageId: 1,
            MessageType.Response,
            new RpcResponse { MessageId = 1, IsSuccess = true },
            ReadOnlySpan<byte>.Empty));

        await call.WaitAsync(Timeout);
    }

    [Fact]
    public async Task InvokeAsync_NoRequestWithResponseBody_ReturnsDeserializedResult()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        var call = peer.InvokeAsync<string>(Service, Method);
        channel.Enqueue(ResponseFrame(serializer, messageId: 1, result: "value"));

        Assert.Equal("value", await call.WaitAsync(Timeout));
    }

    [Fact]
    public async Task InvokeOnInstanceAsync_RoutesThroughInstanceOverloads()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        // Each of the four instance overloads in turn; ids increment 1..4 deterministically.
        var withReqResult = peer.InvokeOnInstanceAsync<int, string>(Service, "inst", Method, request: 1);
        channel.Enqueue(ResponseFrame(serializer, messageId: 1, result: "a"));
        Assert.Equal("a", await withReqResult.WaitAsync(Timeout));

        var resultOnly = peer.InvokeOnInstanceAsync<string>(Service, "inst", Method);
        channel.Enqueue(ResponseFrame(serializer, messageId: 2, result: "b"));
        Assert.Equal("b", await resultOnly.WaitAsync(Timeout));

        var reqVoid = peer.InvokeOnInstanceAsync<string>(Service, "inst", Method, request: "x");
        channel.Enqueue(MessageFramer.FrameMessage(
            serializer, 3, MessageType.Response, new RpcResponse { MessageId = 3, IsSuccess = true }, ReadOnlySpan<byte>.Empty));
        await reqVoid.WaitAsync(Timeout);

        var voidOnly = peer.InvokeOnInstanceAsync(Service, "inst", Method);
        channel.Enqueue(MessageFramer.FrameMessage(
            serializer, 4, MessageType.Response, new RpcResponse { MessageId = 4, IsSuccess = true }, ReadOnlySpan<byte>.Empty));
        await voidOnly.WaitAsync(Timeout);
    }

    [Fact]
    public async Task InvokeAsync_MultipleConcurrentRequests_CorrelatedByMessageId()
    {
        var serializer = NewSerializer();
        await using var channel = new RecordingChannel();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        const int callCount = 8;
        var calls = Enumerable.Range(0, callCount)
            .Select(i => peer.InvokeAsync<int, int>(Service, Method, request: i))
            .ToArray();

        // Wait until the peer has actually sent every request frame so we know all ids.
        var ids = await channel.WaitForSentFrameIdsAsync(callCount, Timeout);

        // Respond out of order to prove correlation is by id, not arrival order: each id i (1-based)
        // gets result id*100.
        foreach (var id in Enumerable.Reverse(ids))
        {
            channel.Enqueue(ResponseFrame(serializer, id, result: id * 100));
        }

        var results = await Task.WhenAll(calls).WaitAsync(Timeout);

        // Each call receives the result keyed to ITS OWN message id (id*100). Because the concurrent
        // sends may reserve ids in any order relative to the calls[] array, assert on the multiset:
        // every id's response was delivered to exactly one awaiting call, and none crossed wires.
        var expected = ids.Select(id => id * 100).OrderBy(v => v).ToArray();
        var actual = results.OrderBy(v => v).ToArray();
        Assert.Equal(expected, actual);
        Assert.Equal(callCount, actual.Distinct().Count());
    }

    [Fact]
    public async Task InvokeAsync_ErrorResponse_ThrowsRemoteServiceException()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        var call = peer.InvokeAsync<int, string>(Service, Method, request: 1);
        channel.Enqueue(ErrorFrame(serializer, messageId: 1, "boom", "MyError"));

        var ex = await Assert.ThrowsAsync<RemoteServiceException>(() => call.WaitAsync(Timeout));
        Assert.Equal("boom", ex.Message);
        Assert.Equal("MyError", ex.RemoteExceptionType);
    }

    [Fact]
    public async Task InvokeAsync_UnsuccessfulResponseWithMissingErrorFields_FaultsWithProtocolException()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        var call = peer.InvokeAsync<int, string>(Service, Method, request: 1);
        channel.Enqueue(MessageFramerTestExtensions.FrameToPayloadWithGarbageEnvelope(
            messageId: 1,
            WriteFailedResponseWithoutErrorDetails()));

        var ex = await Assert.ThrowsAsync<ServiceProtocolException>(() => call.WaitAsync(Timeout));
        Assert.Contains("Malformed response envelope", ex.Message);
    }

    [Fact]
    public async Task InvokeAsync_MalformedResponseEnvelope_FaultsRequestWithProtocolException()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        var call = peer.InvokeAsync<int, string>(Service, Method, request: 1);

        // A well-formed frame header (so the read loop routes it to the outbound invoker) but the
        // envelope bytes are not a valid RpcResponse -> Deserialize throws -> TryFail("Malformed
        // response envelope.").
        var garbageEnvelope = new byte[] { 0xC1 }; // 0xC1 is the MessagePack "never used" byte -> throws.
        var frame = MessageFramerTestExtensions.FrameToPayloadWithGarbageEnvelope(messageId: 1, garbageEnvelope);
        channel.Enqueue(frame);

        var ex = await Assert.ThrowsAsync<ServiceProtocolException>(() => call.WaitAsync(Timeout));
        Assert.Contains("Malformed response envelope", ex.Message);
    }

    private static byte[] WriteFailedResponseWithoutErrorDetails()
    {
        var writer = new ArrayBufferWriter<byte>();
        var message = new MessagePackWriter(writer);
        message.WriteMapHeader(2);
        message.Write("MessageId");
        message.Write(1);
        message.Write("IsSuccess");
        message.Write(false);
        message.Flush();
        return writer.WrittenMemory.ToArray();
    }

}
