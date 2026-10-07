using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class RpcBufferedPipeProbe
{
    private const int Bytes = 1024 * 1024;
    private const int Iterations = 32;

    public static void Run()
    {
        Console.WriteLine("Already-buffered pipe: 1 MiB per transfer, 32 transfers/sample, five samples; setup included.");
        foreach (var segmentSize in new[] { 256, 1024, 4096, 16384, 65536 })
        {
            using var fixture = new Fixture(segmentSize);
            for (var i = 0; i < 8; i++)
            {
                fixture.Transfer();
            }

            var times = new double[5];
            var allocations = new double[5];
            for (var sample = 0; sample < times.Length; sample++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                for (var i = 0; i < Iterations; i++)
                {
                    fixture.Transfer();
                }

                times[sample] = Stopwatch.GetElapsedTime(start).TotalMicroseconds / Iterations;
                allocations[sample] = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / Iterations;
            }

            Array.Sort(times);
            Console.WriteLine($"segment={segmentSize,5} {times[2],9:F1} us/transfer [{times[0]:F1}, {times[4]:F1}] " +
                $"{allocations[2],10:F0} B/transfer frames=credits={fixture.Frames}");
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly int _segmentSize;
        private readonly ExactMemoryPool _pool = new();
        private readonly byte[] _expected = new byte[Bytes];
        private readonly MessagePackRpcSerializer _serializer = new();
        private readonly RpcStreamManager _streams;
        private int _streamId;
        private int _sent;

        public Fixture(int segmentSize)
        {
            _segmentSize = segmentSize;
            new Random(42).NextBytes(_expected);
            _streams = new RpcStreamManager(_serializer, Send, exceptionTransformer: null);
        }

        public int Frames { get; private set; }

        public void Transfer()
        {
            var pipe = new Pipe(new PipeOptions(_pool, minimumSegmentSize: 1, pauseWriterThreshold: 0, resumeWriterThreshold: 0));
            for (var offset = 0; offset < Bytes; offset += _segmentSize)
            {
                _expected.AsMemory(offset, _segmentSize).CopyTo(pipe.Writer.GetMemory(_segmentSize));
                pipe.Writer.Advance(_segmentSize);
            }

            pipe.Writer.FlushAsync().GetAwaiter().GetResult();
            pipe.Writer.Complete();
            var handle = _streams.ReserveOutbound(RpcStreamKind.Binary);
            _streamId = handle.StreamId;
            var attachment = RpcStreamAttachment.FromPipe(handle, pipe, completeReader: true);
            var outbound = _streams.RegisterOutbound(attachment, CancellationToken.None);
            Frames = 0;
            _sent = 0;
            AddCredit(RpcStreamManager.WindowSize);
            try
            {
                attachment.PumpCoreAsync(_streams, _serializer, CancellationToken.None).GetAwaiter().GetResult();
                if (_sent != Bytes)
                {
                    throw new InvalidOperationException("Incomplete transfer.");
                }
            }
            finally
            {
                outbound.DisposeAsync().GetAwaiter().GetResult();
            }
        }

        private Task Send(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var payload = frame.Slice(MessageFramer.HeaderSize);
            if (!payload.Span.SequenceEqual(_expected.AsSpan(_sent, payload.Length)))
            {
                throw new InvalidOperationException("Transfer bytes changed.");
            }

            Frames++;
            _sent += payload.Length;
            AddCredit(1);
            return Task.CompletedTask;
        }

        private void AddCredit(int count)
        {
            using var credit = RpcRawFrame.FrameInt32(_streamId, MessageType.StreamCredit, count);
            if (!_streams.TryAddCredit(credit))
            {
                throw new InvalidOperationException("Credit rejected.");
            }
        }

        public void Dispose()
        {
            _streams.Stop();
            _pool.Dispose();
        }
    }

    private sealed class ExactMemoryPool : MemoryPool<byte>
    {
        public override int MaxBufferSize => int.MaxValue;
        public override IMemoryOwner<byte> Rent(int minBufferSize = -1) => new Buffer(Math.Max(1, minBufferSize));
        protected override void Dispose(bool disposing) { }
        private sealed class Buffer(int size) : IMemoryOwner<byte>
        {
            public Memory<byte> Memory { get; } = new byte[size];
            public void Dispose() { }
        }
    }
}
