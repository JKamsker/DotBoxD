using System.Threading.Tasks.Sources;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Client;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Tests.Protocol.Buffers;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;

[Collection(PooledBufferWriterCacheCollection.Name)]
public sealed class CompletedSendSourceConsumptionTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(InvocationShape.TaskUnary)]
    [InlineData(InvocationShape.PooledUnary)]
    [InlineData(InvocationShape.PooledNoResponse)]
    public async Task Completed_send_source_is_consumed_and_result_failure_releases_setup(
        InvocationShape shape)
    {
        await using var harness = new CompletedSendSourceHarness();
        Task? failed = null;

        var synchronousError = Record.Exception(() =>
        {
            failed = harness.Invoke(shape);
        });

        Assert.Null(synchronousError);
        var sendError = await Assert.ThrowsAsync<IOException>(
            () => failed!.WaitAsync(Guard));
        Assert.Same(harness.SendFailure, sendError);
        Assert.Equal(1, harness.SendResultReadCount);
        harness.AssertTransferredWriterLeaseRemainsActive();

        await harness.Invoke(shape).WaitAsync(Guard);

        Assert.Equal(2, harness.SendResultReadCount);
    }

    public enum InvocationShape
    {
        TaskUnary,
        PooledUnary,
        PooledNoResponse,
    }

    private sealed class CompletedSendSourceHarness : IAsyncDisposable
    {
        private const string Service = "CompletedSend";
        private const string Method = "Invoke";
        private const int ResponseValue = 17;

        private readonly MessagePackRpcSerializer _serializer = new();
        private readonly PrecompletedSendSource _sendSource = new();
        private readonly RpcStreamManager _streams;
        private PooledBufferWriter? _reusedTransferredWriter;
        private InvocationShape _shape;
        private int _sendCount;

        public CompletedSendSourceHarness()
        {
            _streams = new RpcStreamManager(
                _serializer,
                SendMemoryAsync,
                exceptionTransformer: null);
            Invoker = new RpcPeerOutboundInvoker(
                _serializer,
                new RpcPeerOptions
                {
                    EnableLowAllocationValueTaskInvocations = true,
                    MaxPendingRequests = 1,
                    RequestTimeout = Timeout.InfiniteTimeSpan,
                },
                ensureStarted: static () => { },
                SendMemoryAsync,
                SendFrameAsync,
                _streams);
        }

        public RpcPeerOutboundInvoker Invoker { get; }

        public IOException SendFailure => _sendSource.Failure;

        public int SendResultReadCount => _sendSource.ResultReadCount;

        public Task Invoke(InvocationShape shape)
        {
            _shape = shape;
            return shape switch
            {
                InvocationShape.TaskUnary =>
                    Invoker.InvokeAsync<int, int>(Service, Method, request: 1),
                InvocationShape.PooledUnary =>
                    Invoker.InvokeValueAsync<int, int>(Service, Method, request: 1).AsTask(),
                InvocationShape.PooledNoResponse =>
                    Invoker.InvokeValueAsync<int>(Service, Method, request: 1).AsTask(),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(shape),
                    shape,
                    "Unknown invocation shape."),
            };
        }

        public void AssertTransferredWriterLeaseRemainsActive()
        {
            var writer = Assert.IsType<PooledBufferWriter>(_reusedTransferredWriter);
            _ = writer.WrittenMemory;
        }

        public async ValueTask DisposeAsync()
        {
            _reusedTransferredWriter?.Dispose();
            _reusedTransferredWriter = null;
            Invoker.FailPending(new ServiceConnectionException("Test harness disposed."));
            await Invoker.StopCancelFramesAsync().ConfigureAwait(false);
            _streams.Stop();
        }

        private Task SendMemoryAsync(
            ReadOnlyMemory<byte> frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        private ValueTask SendFrameAsync(
            PooledBufferWriter frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MessageFramer.TryReadFrameHeader(
                    frame.WrittenMemory,
                    out var messageId,
                    out var messageType) ||
                messageType != MessageType.Request)
            {
                frame.Dispose();
                throw new InvalidOperationException("Expected an outbound request frame.");
            }

            var transferredWriter = frame;
            frame.Dispose();
            var sendCount = Interlocked.Increment(ref _sendCount);
            if (sendCount == 1)
            {
                // Re-lease the transferred object before GetResult fails: an invalid caller-side
                // dispose after ownership transfer would now dispose this newer writer lease.
                _reusedTransferredWriter = PooledBufferWriter.Rent(MessageFramer.HeaderSize);
                Assert.Same(transferredWriter, _reusedTransferredWriter);
            }

            var send = _sendSource.Issue(failResult: sendCount == 1);
            if (sendCount != 1)
            {
                CompleteResponse(messageId);
            }

            return send;
        }

        private void CompleteResponse(int messageId)
        {
            Payload response;
            if (_shape == InvocationShape.PooledNoResponse)
            {
                response = CreateResponse(messageId, ReadOnlySpan<byte>.Empty);
            }
            else
            {
                using var payload = _serializer.SerializeToPayload(ResponseValue);
                response = CreateResponse(messageId, payload.Memory.Span);
            }

            if (!Invoker.TryCompleteResponse(messageId, response))
            {
                response.Dispose();
                throw new InvalidOperationException("Synthetic response was not accepted.");
            }
        }

        private Payload CreateResponse(int messageId, ReadOnlySpan<byte> payload) =>
            MessageFramer.FrameMessage(
                _serializer,
                messageId,
                MessageType.Response,
                new RpcResponse { MessageId = messageId, IsSuccess = true },
                payload);
    }

    private sealed class PrecompletedSendSource : IValueTaskSource
    {
        private ManualResetValueTaskSourceCore<bool> _source;
        private bool _active;
        private bool _failResult;
        private bool _issued;
        private int _resultReadCount;

        public IOException Failure { get; } = new("Synthetic completed-send result failure.");

        public int ResultReadCount => Volatile.Read(ref _resultReadCount);

        public ValueTask Issue(bool failResult)
        {
            if (_active)
            {
                throw new InvalidOperationException("The previous send source was not consumed.");
            }

            if (_issued)
            {
                _source.Reset();
            }

            _issued = true;
            _active = true;
            _failResult = failResult;
            var token = _source.Version;
            _source.SetResult(true);
            return new ValueTask(this, token);
        }

        void IValueTaskSource.GetResult(short token)
        {
            _source.GetResult(token);
            if (!_active)
            {
                throw new InvalidOperationException("The send source was consumed twice.");
            }

            _active = false;
            Interlocked.Increment(ref _resultReadCount);
            if (_failResult)
            {
                throw Failure;
            }
        }

        ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) =>
            _source.GetStatus(token);

        void IValueTaskSource.OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags) =>
            _source.OnCompleted(continuation, state, token, flags);
    }
}
