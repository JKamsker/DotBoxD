using System.Buffers;
using System.IO.Pipelines;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Remote;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.ContextLifetime;

internal sealed class ContextLifetimeState
{
    public WeakReference? Source { get; set; }
    public int DisposeCalls;
    public int SentFrames;
}

internal sealed class ContextLifetimeSerializer(bool failStreamEnvelope = false) : ISerializer
{
    private readonly MessagePackRpcSerializer _inner = new();
    public void Serialize<T>(IBufferWriter<byte> writer, T value)
    {
        if (failStreamEnvelope && value is RpcResponse { Stream: not null })
        {
            throw new InvalidOperationException("Response serialization failed.");
        }

        _inner.Serialize(writer, value);
    }

    public T Deserialize<T>(ReadOnlyMemory<byte> data) => _inner.Deserialize<T>(data);
    public object? Deserialize(ReadOnlyMemory<byte> data, Type type) => _inner.Deserialize(data, type);
}

internal sealed class ContextLifetimeSender(ContextLifetimeState state)
{
    public Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        Interlocked.Increment(ref state.SentFrames);
        return Task.CompletedTask;
    }
}

internal sealed class ContextLifetimeDispatcher(string mode, ContextLifetimeState state, CancellationTokenSource? cancellation = null) : IServiceDispatcher
{
    public string ServiceName => "ContextLifetime";
    public RpcStreamingContext? Captured { get; private set; }

    public Task DispatchAsync(string method, ReadOnlyMemory<byte> payload, ISerializer serializer,
        IInstanceRegistry registry, IBufferWriter<byte> output, IRpcStreamingContext streaming, CancellationToken ct = default)
    {
        Captured = (RpcStreamingContext)streaming;
        switch (mode)
        {
            case "Unary":
                serializer.Serialize(output, 42);
                break;
            case "Pipe":
                var pipe = new Pipe();
                pipe.Writer.Write(new byte[] { 1, 2, 3 });
                pipe.Writer.Complete();
                state.Source = new WeakReference(pipe);
                streaming.SetResponse(pipe);
                break;
            case "Items":
                var items = Items(state);
                state.Source = new WeakReference(items);
                streaming.SetResponse(items);
                break;
            default:
                var stream = new SourceStream(state, mode == "DisposeFailure");
                state.Source = new WeakReference(stream);
                streaming.SetResponse(stream);
                if (mode is "DispatchFailure" or "DisposeFailure")
                {
                    throw new InvalidOperationException("Dispatch failed.");
                }

                if (mode == "CanceledDispatch")
                {
                    cancellation!.Cancel();
                    ct.ThrowIfCancellationRequested();
                }

                break;
        }

        return Task.CompletedTask;
    }

    public Task DispatchAsync(string method, ReadOnlyMemory<byte> payload, ISerializer serializer,
        IInstanceRegistry registry, IBufferWriter<byte> output, CancellationToken ct = default) => throw new NotSupportedException();

    private static async IAsyncEnumerable<int> Items(ContextLifetimeState state)
    {
        try
        {
            yield return 1;
            await Task.Yield();
            yield return 2;
            yield return 3;
        }
        finally
        {
            Interlocked.Increment(ref state.DisposeCalls);
        }
    }

    private sealed class SourceStream(ContextLifetimeState state, bool failDispose) : MemoryStream(new byte[] { 1, 2, 3 })
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Increment(ref state.DisposeCalls);
                if (failDispose)
                {
                    throw new InvalidOperationException("Source disposal failed.");
                }
            }

            base.Dispose(disposing);
        }
    }
}
