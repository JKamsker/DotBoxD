using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound;

public sealed class RpcStreamCancellationCallbackTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Cancel_ReportsCallbackFailureAndKeepsCancellationRequested()
    {
        using var state = new RpcStreamSendState(1, CancellationToken.None);
        var callbackFailure = new InvalidOperationException("callback failure");
        using var registration = state.Token.Register(() => throw callbackFailure);
        RpcDiagnosticErrorEventArgs? diagnostic = null;
        RpcDiagnostics.Error += OnDiagnostic;
        try
        {
            var error = Record.Exception(state.Cancel);

            Assert.Null(error);
            Assert.True(state.IsCancellationRequested);
            Assert.NotNull(diagnostic);
            Assert.Equal("Outbound stream cancellation callback failed", diagnostic.Operation);
            Assert.Contains(callbackFailure, Assert.IsType<AggregateException>(diagnostic.Error).InnerExceptions);
        }
        finally
        {
            RpcDiagnostics.Error -= OnDiagnostic;
        }

        void OnDiagnostic(object? sender, RpcDiagnosticErrorEventArgs args)
        {
            if (args.Error is AggregateException aggregate && aggregate.InnerExceptions.Contains(callbackFailure))
            {
                diagnostic = args;
            }
        }
    }

    [Fact]
    public async Task Dispose_ReleasesCreditWindowWhenCallbackThrows()
    {
        using var state = new RpcStreamSendState(2, CancellationToken.None);
        using var registration = state.Token.Register(static () => throw new InvalidOperationException("callback failure"));

        var error = Record.Exception(state.Dispose);

        Assert.Null(error);
        Assert.True(state.IsCancellationRequested);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => state.WaitForCreditAsync(CancellationToken.None));
    }

    [Fact]
    public async Task OutboundSet_DisposesEveryOwnedSourceWhenCancellationCallbackThrows()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var first = new BlockingStream(throwOnCancellation: true);
        var second = new BlockingStream(throwOnCancellation: false);
        await using var outbound = streams.RegisterOutbound(
            [
                RpcStreamAttachment.FromStream(streams.ReserveOutbound(RpcStreamKind.Binary), first, leaveOpen: false),
                RpcStreamAttachment.FromStream(streams.ReserveOutbound(RpcStreamKind.Binary), second, leaveOpen: false),
            ],
            CancellationToken.None);
        try
        {
            outbound.Start();
            await Task.WhenAll(first.ReadStarted.Task, second.ReadStarted.Task).WaitAsync(Timeout);

            var error = await Record.ExceptionAsync(() => outbound.DisposeAsync().AsTask().WaitAsync(Timeout));

            Assert.Null(error);
            Assert.Equal(1, first.DisposeCount);
            Assert.Equal(1, second.DisposeCount);
            Assert.Equal(0, streams.OutboundSenderCount);
        }
        finally
        {
            if (first.DisposeCount == 0)
            {
                await first.DisposeAsync();
            }
            if (second.DisposeCount == 0)
            {
                await second.DisposeAsync();
            }
        }
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) => Task.CompletedTask;

    private sealed class BlockingStream(bool throwOnCancellation) : MemoryStream
    {
        private readonly TaskCompletionSource<int> _read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _registration;
        private int _disposeCount;

        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (throwOnCancellation)
            {
                _registration = cancellationToken.Register(static () => throw new InvalidOperationException("source callback failure"));
            }
            ReadStarted.TrySetResult();
            return new ValueTask<int>(_read.Task);
        }

        public override ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            _registration.Dispose();
            _read.TrySetResult(0);
            return base.DisposeAsync();
        }
    }
}
