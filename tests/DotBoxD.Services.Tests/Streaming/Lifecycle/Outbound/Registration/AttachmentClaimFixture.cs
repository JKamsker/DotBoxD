using System.Buffers;
using System.IO.Pipelines;
using System.Reflection;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound.Registration;

internal sealed class AttachmentClaimFixture : IAsyncDisposable
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly List<RpcOutboundStreamSet> _registrations = [];
    private readonly PendingSource _source;
    private readonly Pipe _pipe = new();
    private readonly bool _batch;
    private readonly bool _pendingSend;
    private bool _started;

    internal AttachmentClaimFixture(string operation, bool batch)
    {
        _batch = batch;
        _pendingSend = operation == "PipeSend";
        _source = new PendingSource(operation, Entered, Release);
        Manager = new RpcStreamManager(new MessagePackRpcSerializer(), SendAsync, null);
        var kind = operation.StartsWith("Items", StringComparison.Ordinal) ? RpcStreamKind.Items : RpcStreamKind.Binary;
        var handle = Manager.ReserveOutbound(kind);
        Attachment = operation switch
        {
            "StreamRead" => RpcStreamAttachment.FromStream(handle, _source),
            "PipeSend" => RpcStreamAttachment.FromPipe(handle, _pipe),
            _ => RpcStreamAttachment.FromAsyncEnumerable(handle, _source),
        };
        Original = Register(Manager);
    }

    internal RpcStreamManager Manager { get; }
    internal RpcStreamAttachment Attachment { get; }
    internal RpcOutboundStreamSet Original { get; }
    internal Task Pump { get; private set; } = Task.CompletedTask;
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal async Task StartAsync()
    {
        if (_pendingSend)
        {
            _pipe.Writer.Write(new byte[] { 42 });
            await _pipe.Writer.CompleteAsync();
            using var credit = RpcRawFrame.FrameInt32(Attachment.Handle.StreamId, MessageType.StreamCredit, 1);
            Assert.True(Manager.TryAddCredit(credit));
        }
        _started = true;
        Original.Start();
        var tasks = (Task[]?)typeof(RpcOutboundStreamSet)
            .GetField("_tasks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Original);
        Pump = Task.WhenAll(Assert.IsType<Task[]>(tasks));
        await Entered.Task.WaitAsync(Timeout);
    }

    internal RpcOutboundStreamSet Register(RpcStreamManager manager)
    {
        var registration = _batch
            ? manager.RegisterOutbound([Attachment], CancellationToken.None)
            : manager.RegisterOutbound(Attachment, CancellationToken.None);
        _registrations.Add(registration);
        return registration;
    }

    internal async Task CompletePumpAsync()
    {
        Release.TrySetResult();
        await Pump.WaitAsync(Timeout);
    }

    public async ValueTask DisposeAsync()
    {
        Release.TrySetResult();
        foreach (var registration in _registrations)
        {
            await registration.DisposeAsync();
        }
        await Pump.WaitAsync(Timeout);
        if (!_pendingSend || !_started)
        {
            await _pipe.Writer.CompleteAsync();
        }
        await _pipe.Reader.CompleteAsync();
        await _source.DisposeAsync();
        Manager.Stop();
    }

    private Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Assert.True(MessageFramer.TryReadFrameHeader(frame, out _, out var type));
        if (_pendingSend && type == MessageType.StreamItem)
        {
            Entered.TrySetResult();
            return Release.Task;
        }
        return Task.CompletedTask;
    }

    private sealed class PendingSource(string operation, TaskCompletionSource entered, TaskCompletionSource release)
        : MemoryStream, IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        public int Current => 0;
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

        public async ValueTask<bool> MoveNextAsync()
        {
            if (operation == "ItemsMove")
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
            }
            return false;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return 0;
        }

        public override async ValueTask DisposeAsync()
        {
            if (operation == "ItemsDispose")
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
            }
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
