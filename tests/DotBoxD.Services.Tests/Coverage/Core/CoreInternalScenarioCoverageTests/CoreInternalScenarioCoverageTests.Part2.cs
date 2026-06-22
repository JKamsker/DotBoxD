using System.Buffers;
using System.Threading.Channels;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Transport;
namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed partial class CoreInternalScenarioCoverageTests
{
    private sealed class SendFailingScriptedConnection : IRpcChannel
    {
        private readonly Channel<Payload> _inbound =
            Channel.CreateUnbounded<Payload>(new UnboundedChannelOptions { SingleReader = true });
        private readonly object _gate = new();
        private readonly List<(int Count, TaskCompletionSource<bool> Completion)> _waiters = new();
        private int _sendAttempts;
        private int _disposed;

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "send-failing-scripted://remote";

        public void Enqueue(Payload frame) => _inbound.Writer.TryWrite(frame);

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            CompleteWaiters(Interlocked.Increment(ref _sendAttempts));
            return Task.FromException(new InvalidOperationException("send disabled"));
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            try
            {
                return await _inbound.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                return Payload.Empty;
            }
        }

        public Task WaitForSendAttemptsAsync(int count, TimeSpan timeout)
        {
            TaskCompletionSource<bool> completion;
            lock (_gate)
            {
                if (Volatile.Read(ref _sendAttempts) >= count)
                {
                    return Task.CompletedTask;
                }

                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((count, completion));
            }

            return completion.Task.WaitAsync(timeout);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return default;
            }

            _inbound.Writer.TryComplete();
            while (_inbound.Reader.TryRead(out var frame))
            {
                frame.Dispose();
            }

            return default;
        }

        private void CompleteWaiters(int count)
        {
            List<TaskCompletionSource<bool>>? completed = null;
            lock (_gate)
            {
                for (var i = _waiters.Count - 1; i >= 0; i--)
                {
                    if (count < _waiters[i].Count)
                    {
                        continue;
                    }

                    completed ??= new List<TaskCompletionSource<bool>>();
                    completed.Add(_waiters[i].Completion);
                    _waiters.RemoveAt(i);
                }
            }

            if (completed is null)
            {
                return;
            }

            foreach (var completion in completed)
            {
                completion.TrySetResult(true);
            }
        }
    }

    /// <summary>
    /// An <see cref="IRpcChannel"/> that distinguishes Request sends from Cancel sends. Request sends
    /// succeed and signal <see cref="RequestSent"/>. Cancel sends throw (to drive the cancel-frame send
    /// fault path) and signal <see cref="CancelSendAttempted"/>. Inbound responses are delivered from a
    /// queue so the peer stays usable after the swallowed cancel-send fault.
    /// </summary>
    private sealed class CancelSendControlChannel : IRpcChannel
    {
        private readonly bool _faultCancelSends;
        private readonly Channel<Payload> _inbound =
            Channel.CreateUnbounded<Payload>(new UnboundedChannelOptions { SingleReader = true });
        private int _disposed;

        public CancelSendControlChannel(bool faultCancelSends) => _faultCancelSends = faultCancelSends;

        public TaskCompletionSource<bool> RequestSent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> CancelSendAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "cancel-control://remote";

        public void EnqueueResponse<TResult>(ISerializer serializer, int messageId, TResult result)
        {
            var response = new RpcResponse { MessageId = messageId, IsSuccess = true };
            var payloadWriter = new ArrayBufferWriter<byte>();
            serializer.Serialize(payloadWriter, result);
            _inbound.Writer.TryWrite(MessageFramer.FrameMessage(
                serializer, messageId, MessageType.Response, response, payloadWriter.WrittenSpan));
        }

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            if (!MessageFramer.TryReadFrameHeader(data, out _, out var type))
            {
                return Task.CompletedTask;
            }

            if (type == MessageType.Cancel)
            {
                CancelSendAttempted.TrySetResult(true);
                return _faultCancelSends
                    ? Task.FromException(new InvalidOperationException("cancel send disabled"))
                    : Task.CompletedTask;
            }

            if (type == MessageType.Request)
            {
                RequestSent.TrySetResult(true);
            }

            return Task.CompletedTask;
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            try
            {
                return await _inbound.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                return Payload.Empty;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return default;
            }

            _inbound.Writer.TryComplete();
            while (_inbound.Reader.TryRead(out var frame))
            {
                frame.Dispose();
            }

            return default;
        }
    }

    private sealed class NoopDispatcher : IServiceDispatcher
    {
        public const string Service = "Noop";

        public string ServiceName => Service;

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class BlockingDispatcher : IServiceDispatcher
    {
        public const string Service = "Round2Blocking";

        private readonly TaskCompletionSource<bool> _firstEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ServiceName => Service;

        public Task FirstEntered => _firstEntered.Task;

        public async Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            _firstEntered.TrySetResult(true);
            await _release.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        public void Release() => _release.TrySetResult(true);
    }

}
