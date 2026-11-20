using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Frames;
using DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Transport.SendPhaseTimeout.SendPhaseCalls;

namespace DotBoxD.Services.Tests.Coverage.Transport.SendPhaseTimeout;

/// <summary>
/// A response that arrives while the request send is still in flight proves the remote received the
/// request. When the deadline or caller cancellation then ends the wait for that send, the response
/// still wins on the general path, and the request's outbound streams start: the remote holds the
/// request and waits for their data, so leaving them unstarted would stall it forever.
/// </summary>
public sealed class ResponseBeforeDetachedSendTests
{
    private const int ResponseStreamId = 17_301;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Response_before_detached_send_starts_request_streams(bool callerCancels)
    {
        var serializer = new MessagePackRpcSerializer();
        await using var harness = new ValueTaskTimeoutTestHarness(
            callerCancels ? Timeout.InfiniteTimeSpan : RequestTimeout,
            maxPendingRequests: 1,
            blockRequestSend: true,
            serializer: serializer);
        using var cancellation = new CancellationTokenSource();
        var source = new TrackingSourceStream();
        var handle = harness.Invoker.ReserveStream(RpcStreamKind.Binary);
        var attachment = RpcStreamAttachment.FromStream(handle, source, leaveOpen: false);

        var invocation = harness.Invoker.InvokeStreamAsync(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request: 1,
            attachment,
            cancellation.Token);
        CompleteWithResponseStream(harness, serializer, harness.LastRequestMessageId);
        if (callerCancels)
        {
            cancellation.Cancel();
        }

        // The deadline or caller cancellation detaches the stuck send; the delivered response wins.
        await using var response = await invocation.WaitAsync(Guard);

        // The remote already has the request, so its outbound stream is pumped.
        await WaitUntilAsync(() => source.ReadCount > 0);

        // The remote answered, so the late write needs no cancel frame.
        harness.ReleaseRequestSend();
        await InvokeAndCompleteWhenAdmittedAsync(harness, SendPhaseCall.TaskUnary);
        Assert.Equal(0, harness.CancelCount);
    }

    private static void CompleteWithResponseStream(
        ValueTaskTimeoutTestHarness harness,
        MessagePackRpcSerializer serializer,
        int messageId)
    {
        var response = MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Response,
            new RpcResponse
            {
                MessageId = messageId,
                IsSuccess = true,
                Stream = new RpcStreamHandle(ResponseStreamId, RpcStreamKind.Binary),
            },
            ReadOnlySpan<byte>.Empty);
        if (!harness.Invoker.TryCompleteResponse(messageId, response))
        {
            response.Dispose();
            throw new InvalidOperationException("Synthetic streaming response was not accepted.");
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Guard;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The request's outbound stream never started.");
            await Task.Delay(5);
        }
    }
}
