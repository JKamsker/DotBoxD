using System.Buffers;
using System.IO.Pipelines;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound.Chunking;

internal sealed class PipeChunkingFixture : IAsyncDisposable
{
    public const int ChunkSize = 64 * 1024;
    private readonly ExactMemoryPool _pool = new();
    private readonly RpcStreamManager _streams;
    private readonly RpcOutboundStreamSet _outbound;
    private readonly RpcStreamAttachment _attachment;
    private readonly MessagePackRpcSerializer _serializer = new();
    private bool _writerCompleted;

    private PipeChunkingFixture(int[] segments, bool owned, int credits)
    {
        Owned = owned;
        Expected = new byte[segments.Sum()];
        new Random(42).NextBytes(Expected);
        Pipe = new Pipe(new PipeOptions(_pool, minimumSegmentSize: 1, pauseWriterThreshold: 0, resumeWriterThreshold: 0));
        var offset = 0;
        foreach (var length in segments)
        {
            if (length != 0)
            {
                Expected.AsMemory(offset, length).CopyTo(Pipe.Writer.GetMemory(length));
                Pipe.Writer.Advance(length);
                offset += length;
            }
        }

        _streams = new RpcStreamManager(_serializer, Send, exceptionTransformer: null);
        var handle = _streams.ReserveOutbound(RpcStreamKind.Binary);
        _attachment = RpcStreamAttachment.FromPipe(handle, Pipe, completeReader: owned);
        _outbound = _streams.RegisterOutbound(_attachment, CancellationToken.None);
        AddCredit(credits);
    }

    public Pipe Pipe { get; }
    public byte[] Expected { get; }
    public bool Owned { get; }
    public CancellationTokenSource Cancellation { get; } = new();
    public List<int> SentLengths { get; } = [];
    public int BytesSent { get; private set; }
    public int Attempts { get; private set; }
    public bool ReplenishCredit { get; set; } = true;
    public int FailAt { get; set; }
    public bool DeferFailure { get; set; }
    public IOException ExpectedFailure { get; } = new("Simulated chunk send failure.");
    public TaskCompletionSource SendFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static async Task<PipeChunkingFixture> Create(int[] segments, bool owned,
        bool writerCompleted = true, int credits = RpcStreamManager.WindowSize)
    {
        var fixture = new PipeChunkingFixture(segments, owned, credits);
        await fixture.Pipe.Writer.FlushAsync();
        if (writerCompleted)
        {
            await fixture.CompleteWriter();
        }

        return fixture;
    }

    public Task Pump() => _attachment.PumpCoreAsync(_streams, _serializer, Cancellation.Token);

    public async Task CompleteWriter()
    {
        if (!_writerCompleted)
        {
            _writerCompleted = true;
            await Pipe.Writer.CompleteAsync();
        }
    }

    public async Task AssertRemaining(int offset)
    {
        if (Owned)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Pipe.Reader.ReadAsync().AsTask());
            return;
        }

        var read = await Pipe.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.Equal(Expected.AsSpan(offset).ToArray(), read.Buffer.ToArray());
        }
        finally
        {
            Pipe.Reader.AdvanceTo(read.Buffer.End);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Cancellation.Cancel();
        SendFailure.TrySetCanceled(Cancellation.Token);
        await _outbound.DisposeAsync();
        await Pipe.Reader.CompleteAsync();
        await CompleteWriter();
        Cancellation.Dispose();
        _pool.Dispose();
    }

    private Task Send(ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Assert.True(MessageFramer.TryReadFrameHeader(frame, out _, out var type));
        Assert.Equal(MessageType.StreamItem, type);
        Attempts++;
        if (Attempts == FailAt)
        {
            if (DeferFailure)
            {
                return SendFailure.Task;
            }

            throw ExpectedFailure;
        }

        var payload = frame.Slice(MessageFramer.HeaderSize);
        Assert.True(payload.Span.SequenceEqual(Expected.AsSpan(BytesSent, payload.Length)), "Chunk contents or ordering changed.");
        BytesSent += payload.Length;
        SentLengths.Add(payload.Length);
        if (ReplenishCredit)
        {
            AddCredit(1);
        }

        return Task.CompletedTask;
    }

    private void AddCredit(int count)
    {
        if (count > 0)
        {
            using var credit = RpcRawFrame.FrameInt32(_attachment.Handle.StreamId, MessageType.StreamCredit, count);
            Assert.True(_streams.TryAddCredit(credit));
        }
    }

    private sealed class ExactMemoryPool : MemoryPool<byte>
    {
        public override int MaxBufferSize => int.MaxValue;
        public override IMemoryOwner<byte> Rent(int minBufferSize = -1) => new Buffer(Math.Max(1, minBufferSize));
        protected override void Dispose(bool disposing) { }

        private sealed class Buffer(int size) : IMemoryOwner<byte>
        {
            private byte[]? _bytes = new byte[size];
            public Memory<byte> Memory => _bytes ?? throw new ObjectDisposedException(nameof(Buffer));
            public void Dispose() => _bytes = null;
        }
    }
}
