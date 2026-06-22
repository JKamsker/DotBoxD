using System.Buffers;
using System.Collections.Concurrent;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using Xunit;
namespace DotBoxD.Services.Tests.Coverage.Peer;

public sealed partial class PeerOutboundCoverageTests
{
    [Fact]
    public async Task InvokeAsync_ExceedingMaxPendingRequests_ThrowsServiceException()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(
            channel,
            serializer,
            new RpcPeerOptions
            {
                MaxPendingRequests = 1,
                RequestTimeout = TimeSpan.FromSeconds(30),
            }).Start();
        // First call occupies the single slot and parks awaiting a response.
        var first = peer.InvokeAsync<int, string>(Service, Method, request: 1);
        // Second call cannot reserve a slot (pendingCount would exceed 1) -> ServiceException.
        var ex = await Assert.ThrowsAsync<ServiceException>(
            () => peer.InvokeAsync<int, string>(Service, Method, request: 2).WaitAsync(Timeout));
        Assert.Contains("Maximum pending requests", ex.Message);
        // Complete the first so disposal does not have to fault it.
        channel.Enqueue(ResponseFrame(serializer, messageId: 1, result: "done"));
        Assert.Equal("done", await first.WaitAsync(Timeout));
    }
    /// <summary>
    /// An <see cref="IRpcChannel"/> that records every framed message it is asked to send (so tests can
    /// learn the message ids the peer assigned) and lets tests enqueue inbound frames. Reuses the same
    /// unbounded-inbound + receive-count machinery shape as <see cref="ScriptedConnection"/>.
    /// </summary>
    private sealed class RecordingChannel : IRpcChannel
    {
        private readonly System.Threading.Channels.Channel<Payload> _inbound =
            System.Threading.Channels.Channel.CreateUnbounded<Payload>(
                new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true });
        private readonly ConcurrentQueue<(int MessageId, MessageType Type)> _sent = new();
        private readonly object _gate = new();
        private readonly List<(int Count, TaskCompletionSource<int[]> Completion)> _idWaiters = new();
        private readonly List<(MessageType Type, TaskCompletionSource<(int, MessageType)> Completion)> _typeWaiters = new();
        private int _disposed;
        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "recording://remote";

        public void Enqueue(Payload frame) => _inbound.Writer.TryWrite(frame);

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            if (MessageFramer.TryReadFrameHeader(data, out var messageId, out var type))
            {
                _sent.Enqueue((messageId, type));
                Notify(messageId, type);
            }

            return Task.CompletedTask;
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            try
            {
                return await _inbound.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (System.Threading.Channels.ChannelClosedException)
            {
                return Payload.Empty;
            }
        }

        public async Task<int[]> WaitForSentFrameIdsAsync(int count, TimeSpan timeout)
        {
            TaskCompletionSource<int[]> completion;
            lock (_gate)
            {
                var ids = RequestIds();
                if (ids.Length >= count)
                {
                    return ids.Take(count).ToArray();
                }

                completion = new TaskCompletionSource<int[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                _idWaiters.Add((count, completion));
            }

            return await completion.Task.WaitAsync(timeout).ConfigureAwait(false);
        }

        public async Task<(int MessageId, MessageType Type)> WaitForFrameOfTypeAsync(MessageType type, TimeSpan timeout)
        {
            TaskCompletionSource<(int, MessageType)> completion;
            lock (_gate)
            {
                foreach (var sent in _sent)
                {
                    if (sent.Type == type)
                    {
                        return sent;
                    }
                }

                completion = new TaskCompletionSource<(int, MessageType)>(TaskCreationOptions.RunContinuationsAsynchronously);
                _typeWaiters.Add((type, completion));
            }

            return await completion.Task.WaitAsync(timeout).ConfigureAwait(false);
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

        private int[] RequestIds() =>
            _sent.Where(s => s.Type == MessageType.Request).Select(s => s.MessageId).ToArray();

        private void Notify(int messageId, MessageType type)
        {
            List<TaskCompletionSource<int[]>>? idReady = null;
            List<(TaskCompletionSource<(int, MessageType)>, int)>? typeReady = null;
            lock (_gate)
            {
                if (type == MessageType.Request)
                {
                    var ids = RequestIds();
                    for (var i = _idWaiters.Count - 1; i >= 0; i--)
                    {
                        if (ids.Length >= _idWaiters[i].Count)
                        {
                            idReady ??= new List<TaskCompletionSource<int[]>>();
                            idReady.Add(_idWaiters[i].Completion);
                            _idWaiters.RemoveAt(i);
                        }
                    }
                }

                for (var i = _typeWaiters.Count - 1; i >= 0; i--)
                {
                    if (_typeWaiters[i].Type == type)
                    {
                        typeReady ??= new List<(TaskCompletionSource<(int, MessageType)>, int)>();
                        typeReady.Add((_typeWaiters[i].Completion, messageId));
                        _typeWaiters.RemoveAt(i);
                    }
                }
            }

            if (idReady is not null)
            {
                var snapshot = RequestIds();
                foreach (var w in idReady)
                {
                    w.TrySetResult(snapshot);
                }
            }

            if (typeReady is not null)
            {
                foreach (var (completion, id) in typeReady)
                {
                    completion.TrySetResult((id, type));
                }
            }
        }
    }

    /// <summary>An <see cref="IRpcChannel"/> whose <see cref="SendAsync"/> always throws.</summary>
    private sealed class ThrowingSendChannel : IRpcChannel
    {
        private readonly Exception _error;
        private int _disposed;

        public ThrowingSendChannel(Exception error) => _error = error;

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "throwing://remote";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            Task.FromException(_error);

        public Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<Payload>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetResult(Payload.Empty));
            return tcs.Task;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _disposed, 1);
            return default;
        }
    }

    /// <summary>
    /// An <see cref="IRpcChannel"/> whose send can be toggled to fail; otherwise it behaves like a
    /// scripted connection (no-op send, inbound queue) so the peer remains usable after recovery.
    /// </summary>
    private sealed class ToggleSendChannel : IRpcChannel
    {
        private readonly System.Threading.Channels.Channel<Payload> _inbound =
            System.Threading.Channels.Channel.CreateUnbounded<Payload>(
                new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true });
        private int _disposed;

        public bool FailNextSends { get; set; }

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "toggle://remote";

        public void Enqueue(Payload frame) => _inbound.Writer.TryWrite(frame);

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            FailNextSends
                ? Task.FromException(new InvalidOperationException("send disabled"))
                : Task.CompletedTask;

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            try
            {
                return await _inbound.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (System.Threading.Channels.ChannelClosedException)
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
}

/// <summary>
/// Test-only framing helper that builds a syntactically valid frame (correct header, envelope-length
/// prefix, exact total length) whose envelope bytes are deliberately not a valid RpcResponse. Used to
/// drive the "malformed response envelope" fault path through the read loop. Lives in the test
/// assembly only; it reuses the public <see cref="MessageFramer"/> header constants.
/// </summary>
internal static class MessageFramerTestExtensions
{
    public static Payload FrameToPayloadWithGarbageEnvelope(int messageId, byte[] garbageEnvelope) =>
        Build(messageId, garbageEnvelope);

    private static Payload Build(int messageId, byte[] garbageEnvelope)
    {
        var totalLength = MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize + garbageEnvelope.Length;
        var frame = Payload.Rent(totalLength);
        var span = frame.Memory.Span;

        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), totalLength);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), messageId);
        span[8] = (byte)MessageType.Response;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            span.Slice(MessageFramer.HeaderSize, MessageFramer.EnvelopeLengthSize),
            garbageEnvelope.Length);
        garbageEnvelope.CopyTo(span.Slice(MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize));

        return frame;
    }

}
