using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Remote;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core.RemoteStream;

internal sealed class RemoteStreamLifetimeFixture
{
    public Stream? Stream { get; private init; }
    public required WeakReference Sender { get; init; }
    public required WeakReference Receiver { get; init; }
    public required SendState State { get; init; }
    public Task<int>? PendingRead { get; set; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static RemoteStreamLifetimeFixture Create(string stage, bool asynchronousDispose, bool retainStream = true, bool pauseCancel = false)
    {
        var state = new SendState();
        if (!pauseCancel)
        {
            state.AllowCancel.SetResult(true);
        }

        var sender = new SenderTarget(state);
        var manager = new RpcStreamManager(new MessagePackRpcSerializer(), sender.SendAsync, exceptionTransformer: null);
        var handle = new RpcStreamHandle(1, RpcStreamKind.Binary);
        var receiver = manager.RegisterInboundResponse(handle, CancellationToken.None);
        Stream stream = new RpcRemoteStream(receiver);
        if (stage is "Buffered" or "Partial" or "Consumed" or "Live")
        {
            Assert.True(manager.TryAcceptItem(handle.StreamId,
                MessageFramer.FrameToPayload(handle.StreamId, MessageType.StreamItem, new byte[] { 1, 2 })));
        }

        if (stage is "Partial" or "Consumed")
        {
            var buffer = new byte[stage == "Partial" ? 1 : 2];
            Assert.Equal(buffer.Length, stream.ReadAsync(buffer).AsTask().GetAwaiter().GetResult());
        }

        var pending = stage == "Pending" ? stream.ReadAsync(new byte[1]).AsTask() : null;
        if (pending is not null)
        {
            Assert.False(pending.IsCompleted);
        }

        if (stage != "Live")
        {
            Dispose(stream, asynchronousDispose);
            manager.Stop();
        }

        return new RemoteStreamLifetimeFixture
        {
            Stream = retainStream ? stream : null,
            Sender = new WeakReference(sender),
            Receiver = new WeakReference(receiver),
            State = state,
            PendingRead = pending,
        };
    }

    public static void Dispose(Stream stream, bool asynchronous)
    {
        if (asynchronous)
        {
            stream.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        else
        {
            stream.Dispose();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<RemoteStreamLifetimeFixture> RaceReadAndDispose(bool arrayRead)
    {
        var state = new SendState();
        state.AllowCancel.SetResult(true);
        var sender = new SenderTarget(state);
        var manager = new RpcStreamManager(new MessagePackRpcSerializer(), sender.SendAsync, exceptionTransformer: null);
        var handle = new RpcStreamHandle(1, RpcStreamKind.Binary);
        var receiver = manager.RegisterInboundResponse(handle, CancellationToken.None);
        Stream stream = new RpcRemoteStream(receiver);
        var buffer = new byte[1];
        var read = arrayRead ? stream.ReadAsync(buffer, 0, 1) : stream.ReadAsync(buffer).AsTask();
        Assert.False(read.IsCompleted);
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deliver = Task.Run(async () =>
        {
            await start.Task;
            manager.TryAcceptItem(handle.StreamId, MessageFramer.FrameToPayload(handle.StreamId, MessageType.StreamItem, new byte[] { 1 }));
        });
        var dispose = Task.Run(async () =>
        {
            await start.Task;
            stream.Dispose();
        });
        start.SetResult(true);
        await Task.WhenAll(deliver, dispose).WaitAsync(TimeSpan.FromSeconds(5));
        var error = await Record.ExceptionAsync(() => read.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(error is null or OperationCanceledException or ObjectDisposedException, error?.ToString());
        if (error is null)
        {
            Assert.Equal(1, await read);
        }

        manager.Stop();
        return new RemoteStreamLifetimeFixture
        {
            Stream = stream,
            Sender = new WeakReference(sender),
            Receiver = new WeakReference(receiver),
            State = state,
        };
    }

    public async Task AssertCollected()
    {
        var elapsed = Stopwatch.StartNew();
        do
        {
            Collect();
            if (!Sender.IsAlive && !Receiver.IsAlive)
            {
                return;
            }

            await Task.Delay(10);
        }
        while (elapsed.Elapsed < TimeSpan.FromSeconds(5));

        Assert.False(Sender.IsAlive, "A retained disposed stream must release its sender callback.");
        Assert.False(Receiver.IsAlive, "A retained disposed stream must release its receiver and current chunk.");
    }

    public static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public sealed class SendState
    {
        public int Credits;
        public int Cancels;
        public int CompletedCancels;
        public TaskCompletionSource<bool> AllowCancel { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> CancelFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class SenderTarget(SendState state)
    {
        public Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            Assert.True(MessageFramer.TryReadFrameHeader(frame, out _, out var type));
            if (type == MessageType.StreamCredit)
            {
                Interlocked.Increment(ref state.Credits);
            }
            else if (type == MessageType.StreamCancel)
            {
                Interlocked.Increment(ref state.Cancels);
                return CompleteCancelAsync();
            }

            return Task.CompletedTask;
        }

        private async Task CompleteCancelAsync()
        {
            await state.AllowCancel.Task;
            Interlocked.Increment(ref state.CompletedCancels);
            state.CancelFinished.SetResult(true);
            GC.KeepAlive(this);
        }
    }
}
