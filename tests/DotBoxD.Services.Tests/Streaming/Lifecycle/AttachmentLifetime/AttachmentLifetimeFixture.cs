using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.AttachmentLifetime;

internal sealed record AttachmentLifetimeFixture(RpcStreamAttachment? Attachment, WeakReference Source, AttachmentLifetimeState State)
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<AttachmentLifetimeFixture> Exercise(string operation, bool retain)
    {
        var fixture = Create(operation);
        var attachment = fixture.Attachment!;
        if (operation.EndsWith("Direct", StringComparison.Ordinal))
        {
            await attachment.DisposeSourceOnceAsync();
        }
        else
        {
            await using var outbound = NewManager().RegisterOutbound(attachment, CancellationToken.None);
            if (!operation.EndsWith("Unstarted", StringComparison.Ordinal))
            {
                outbound.Start();
                await outbound.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        if (operation.StartsWith("Stream", StringComparison.Ordinal))
        {
            Assert.Equal(1, fixture.State.DisposeCalls);
        }

        return retain ? fixture : fixture with { Attachment = null };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static AttachmentLifetimeFixture Create(string operation, bool owned = true)
    {
        var state = new AttachmentLifetimeState();
        var handle = new RpcStreamHandle(1, RpcStreamKind.Binary);
        if (operation.StartsWith("Pipe", StringComparison.Ordinal))
        {
            var pipe = new Pipe();
            pipe.Writer.Complete(operation == "PipeFailedRead" ? new IOException("Read failed") : null);
            return new AttachmentLifetimeFixture(RpcStreamAttachment.FromPipe(handle, pipe, completeReader: owned),
                new WeakReference(pipe), state);
        }

        var stream = new AttachmentLifetimeStream(state, operation);
        return new AttachmentLifetimeFixture(RpcStreamAttachment.FromStream(handle, stream, leaveOpen: !owned),
            new WeakReference(stream), state);
    }

    public static RpcStreamManager NewManager() => new(new MessagePackRpcSerializer(),
        static (_, _) => Task.CompletedTask, exceptionTransformer: null);

    public async Task AssertCollected()
    {
        var timer = Stopwatch.StartNew();
        do
        {
            Collect();
            if (!Source.IsAlive)
            {
                return;
            }

            await Task.Delay(10);
        }
        while (timer.Elapsed < TimeSpan.FromSeconds(5));

        Assert.False(Source.IsAlive, "Completed owned attachments must release their source.");
    }

    public static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}

internal sealed class AttachmentLifetimeState
{
    public int DisposeCalls;
    public int ReadCalls;
    public TaskCompletionSource<int> ReadCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource DisposeCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class AttachmentLifetimeStream(AttachmentLifetimeState state, string operation) : MemoryStream
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref state.ReadCalls);
        return operation switch
        {
            "StreamFailedRead" => ValueTask.FromException<int>(new IOException("Read failed")),
            "StreamPendingRead" => new ValueTask<int>(state.ReadCompletion.Task),
            _ => new ValueTask<int>(0),
        };
    }

    public override ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref state.DisposeCalls);
        return operation switch
        {
            "StreamFailedDispose" => ValueTask.FromException(new IOException("Dispose failed")),
            "StreamPendingDispose" => new ValueTask(state.DisposeCompletion.Task),
            _ => default,
        };
    }
}
