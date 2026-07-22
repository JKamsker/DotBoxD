using System.Buffers;
using System.Diagnostics;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Client;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class FiniteTimeoutValueTaskUnaryProbe
{
    private const int WarmupIterations = 2_000;
    private const int MeasurementIterations = 50_000;

    public static async Task RunAsync()
    {
        var taskBacked = await MeasureAsync(
            "finite task-backed",
            enableLowAllocation: false,
            TimeSpan.FromSeconds(30));
        var finiteOptIn = await MeasureAsync(
            "finite opt-in",
            enableLowAllocation: true,
            TimeSpan.FromSeconds(30));
        var pooledControl = await MeasureAsync(
            "infinite pooled control",
            enableLowAllocation: true,
            Timeout.InfiniteTimeSpan);

        Console.WriteLine("finite-timeout ValueTask unary probe");
        Console.WriteLine("case                          total ms       ns/op    allocated B      B/op");
        Write(taskBacked);
        Write(finiteOptIn);
        Write(pooledControl);
    }

    private static async Task<Measurement> MeasureAsync(
        string name,
        bool enableLowAllocation,
        TimeSpan timeout)
    {
        await using var harness = new UnaryHarness(enableLowAllocation, timeout);
        for (var i = 0; i < WarmupIterations; i++)
        {
            _ = harness.InvokeOnce();
        }

        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startedAt = Stopwatch.GetTimestamp();
        long checksum = 0;
        for (var i = 0; i < MeasurementIterations; i++)
        {
            checksum += harness.InvokeOnce();
        }

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var expectedChecksum = (long)MeasurementIterations * UnaryHarness.ResponseValue;
        if (checksum != expectedChecksum)
        {
            throw new InvalidOperationException(
                $"response checksum changed: expected {expectedChecksum}, got {checksum}");
        }

        return new Measurement(name, elapsed.TotalMilliseconds, allocated);
    }

    private static void Write(Measurement measurement)
    {
        var nanosecondsPerOperation =
            measurement.ElapsedMilliseconds * 1_000_000 / MeasurementIterations;
        var bytesPerOperation = measurement.AllocatedBytes / (double)MeasurementIterations;
        Console.WriteLine(
            $"{measurement.Name,-28} {measurement.ElapsedMilliseconds,8:N1} " +
            $"{nanosecondsPerOperation,11:N1} {measurement.AllocatedBytes,14:N0} " +
            $"{bytesPerOperation,10:N1}");
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class UnaryHarness : IAsyncDisposable
    {
        public const int ResponseValue = 11;

        private readonly MessagePackRpcSerializer _serializer = new();
        private readonly RpcStreamManager _streams;
        private readonly byte[] _responsePayload;
        private int _messageId;

        public UnaryHarness(bool enableLowAllocation, TimeSpan timeout)
        {
            var payloadWriter = new ArrayBufferWriter<byte>();
            _serializer.Serialize(payloadWriter, ResponseValue);
            _responsePayload = payloadWriter.WrittenSpan.ToArray();
            _streams = new RpcStreamManager(_serializer, SendAsync, exceptionTransformer: null);
            Invoker = new RpcPeerOutboundInvoker(
                _serializer,
                new RpcPeerOptions
                {
                    EnableLowAllocationValueTaskInvocations = enableLowAllocation,
                    RequestTimeout = timeout
                },
                ensureStarted: static () => { },
                SendAsync,
                SendFrameAsync,
                _streams);
        }

        private RpcPeerOutboundInvoker Invoker { get; }

        public int InvokeOnce()
        {
            var messageId = ++_messageId;
            var call = Invoker.InvokeValueAsync<int, int>("Probe", "Unary", request: 7);
            var response = MessageFramer.FrameMessage(
                _serializer,
                messageId,
                MessageType.Response,
                new RpcResponse { MessageId = messageId, IsSuccess = true },
                _responsePayload);
            if (!Invoker.TryCompleteResponse(messageId, response))
            {
                response.Dispose();
                throw new InvalidOperationException("the synthetic response was not accepted");
            }

            if (!call.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("the synthetic unary call did not complete synchronously");
            }

            return call.Result;
        }

        public async ValueTask DisposeAsync()
        {
            Invoker.FailPending(new InvalidOperationException("probe disposed"));
            await Invoker.StopCancelFramesAsync().ConfigureAwait(false);
            _streams.Stop();
        }

        private static Task SendAsync(
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        private static ValueTask SendFrameAsync(
            PooledBufferWriter frame,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return default;
            }
            finally
            {
                frame.Dispose();
            }
        }
    }

    private readonly record struct Measurement(
        string Name,
        double ElapsedMilliseconds,
        long AllocatedBytes);
}
