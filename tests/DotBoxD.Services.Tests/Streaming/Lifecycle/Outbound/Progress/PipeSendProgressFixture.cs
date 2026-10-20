using System.Buffers;
using System.IO.Pipelines;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound.Progress;

internal sealed class PipeSendProgressFixture : IAsyncDisposable
{
    private readonly string _mode;
    private readonly RpcStreamManager _streams;
    private readonly RpcOutboundStreamSet _outbound;
    private readonly TaskCompletionSource _sendFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _writerCompleted;

    private PipeSendProgressFixture(string mode, bool owned)
    {
        _mode = mode;
        _streams = new RpcStreamManager(Serializer, SendAsync, exceptionTransformer: null);
        var handle = _streams.ReserveOutbound(RpcStreamKind.Binary);
        Attachment = RpcStreamAttachment.FromPipe(handle, Pipe, completeReader: owned);
        _outbound = _streams.RegisterOutbound(Attachment, CancellationToken.None);
        if (mode != "BeforeCredit")
        {
            AddCredit(_streams, handle, mode == "AfterFirstCredit" ? 1 : RpcStreamManager.WindowSize);
        }
    }

    public Pipe Pipe { get; } = new(new PipeOptions(minimumSegmentSize: 65536, pauseWriterThreshold: 0, resumeWriterThreshold: 0));
    public MessagePackRpcSerializer Serializer { get; } = new();
    public CancellationTokenSource Cancellation { get; } = new();
    public RpcStreamAttachment Attachment { get; }
    public List<byte[]> Payloads { get; } = [];
    public List<byte[]> Sent { get; } = [];
    public IOException ExpectedFailure { get; } = new("Send failed before accepting the segment.");
    public int SendAttempts { get; private set; }
    public int ExpectedSentBeforeFailure => _mode is "AfterFirstCredit" or "SyncFailureAfterFirst" or "AsyncFailureAfterFirst" or "CancelAfterSuccess" ? 1 : 0;

    public static async Task<PipeSendProgressFixture> Create(string mode, bool writerCompleted, bool owned = false, int? segments = null)
    {
        var fixture = new PipeSendProgressFixture(mode, owned);
        for (var i = 0; i < (segments ?? fixture.ExpectedSentBeforeFailure + 1); i++)
        {
            // Fill the returned memory completely so the next write must use another segment.
            var memory = fixture.Pipe.Writer.GetMemory(4096);
            memory.Span.Fill((byte)(i + 1));
            fixture.Payloads.Add(memory.ToArray());
            fixture.Pipe.Writer.Advance(memory.Length);
        }

        await fixture.Pipe.Writer.FlushAsync();
        if (writerCompleted)
        {
            await fixture.CompleteWriter();
        }

        return fixture;
    }

    public Task Pump(CancellationToken ct = default) => Attachment.PumpCoreAsync(_streams, Serializer, ct);

    public async Task FailPump()
    {
        var pump = Pump(Cancellation.Token);
        Assert.Equal(ExpectedSentBeforeFailure, Sent.Count);
        if (_mode.StartsWith("AsyncFailure", StringComparison.Ordinal))
        {
            Assert.False(pump.IsCompleted);
            Assert.Equal(ExpectedSentBeforeFailure + 1, SendAttempts);
            _sendFailure.SetException(ExpectedFailure);
        }
        else if (_mode is "BeforeCredit" or "AfterFirstCredit" or "SendCanceled")
        {
            Assert.False(pump.IsCompleted);
            Cancellation.Cancel();
        }

        if (_mode.Contains("Failure", StringComparison.Ordinal))
        {
            Assert.Same(ExpectedFailure, await Assert.ThrowsAsync<IOException>(() => pump.WaitAsync(TimeSpan.FromSeconds(5))));
        }
        else
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pump.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(Cancellation.Token, error.CancellationToken);
        }
    }

    public async Task CompleteWriter()
    {
        if (!_writerCompleted)
        {
            _writerCompleted = true;
            await Pipe.Writer.CompleteAsync();
        }
    }

    public async Task<byte[]> ReadRemaining()
    {
        var result = await Pipe.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            return result.Buffer.ToArray();
        }
        finally
        {
            Pipe.Reader.AdvanceTo(result.Buffer.End);
        }
    }

    public async Task<byte[]> Resume()
    {
        await _outbound.DisposeAsync();
        var resumed = new List<byte>();
        var streams = new RpcStreamManager(Serializer, (frame, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            if (MessageFramer.TryReadFrameHeader(frame, out _, out var type) && type == MessageType.StreamItem)
            {
                resumed.AddRange(frame.Slice(MessageFramer.HeaderSize).ToArray());
            }

            return Task.CompletedTask;
        }, exceptionTransformer: null);
        await using var outbound = streams.RegisterOutbound(Attachment, CancellationToken.None);
        AddCredit(streams, Attachment.Handle, RpcStreamManager.WindowSize);
        outbound.Start();
        await outbound.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        return resumed.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await _outbound.DisposeAsync();
        await Pipe.Reader.CompleteAsync();
        await CompleteWriter();
        Cancellation.Dispose();
    }

    private Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        Assert.True(MessageFramer.TryReadFrameHeader(frame, out _, out var type));
        if (type != MessageType.StreamItem)
        {
            return Task.CompletedTask;
        }

        SendAttempts++;
        if (Sent.Count == ExpectedSentBeforeFailure && _mode.Contains("Failure", StringComparison.Ordinal))
        {
            if (_mode.StartsWith("Sync", StringComparison.Ordinal))
            {
                throw ExpectedFailure;
            }

            return _sendFailure.Task;
        }

        if (_mode == "SendCanceled")
        {
            return Task.Delay(Timeout.Infinite, ct);
        }

        Sent.Add(frame.Slice(MessageFramer.HeaderSize).ToArray());
        if (_mode == "CancelAfterSuccess")
        {
            Cancellation.Cancel();
        }

        return Task.CompletedTask;
    }

    private static void AddCredit(RpcStreamManager streams, RpcStreamHandle handle, int count)
    {
        using var credit = RpcRawFrame.FrameInt32(handle.StreamId, MessageType.StreamCredit, count);
        Assert.True(streams.TryAddCredit(credit));
    }
}
