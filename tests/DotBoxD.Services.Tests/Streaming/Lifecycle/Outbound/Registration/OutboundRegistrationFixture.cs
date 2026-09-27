using System.Reflection;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound.Registration;

internal sealed class OutboundRegistrationFixture : IAsyncDisposable
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly PendingStream _source = new();
    private readonly List<MemoryStream> _replacementSources = [];
    private readonly List<RpcOutboundStreamSet> _replacements = [];
    private readonly bool _batch;
    private RpcOutboundStreamSet? _original;
    private Task _pumps = Task.CompletedTask;

    internal OutboundRegistrationFixture(bool batch)
    {
        _batch = batch;
        Manager = new RpcStreamManager(new MessagePackRpcSerializer(), SendAsync, null);
        Handle = Manager.ReserveOutbound(RpcStreamKind.Binary);
    }

    internal RpcStreamManager Manager { get; }
    internal RpcStreamHandle Handle { get; }
    internal List<byte[]> SentItems { get; } = [];

    internal async Task PrepareAsync(int mode)
    {
        if (mode == 0)
        {
            _source.Release.TrySetResult(0);
        }
        _original = Register(RpcStreamAttachment.FromStream(Handle, _source));
        _original.Start();
        // Read the published tasks to await actual pump completion after cancellation. WaitAsync
        // deliberately stops waiting when the owner is canceled, even if a source ignores it.
        var tasks = (Task[]?)typeof(RpcOutboundStreamSet)
            .GetField("_tasks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_original);
        _pumps = Task.WhenAll(Assert.IsType<Task[]>(tasks));
        await _source.Started.Task.WaitAsync(Timeout);
        if (mode == 0)
        {
            await _pumps.WaitAsync(Timeout);
        }
        else if (mode == 1)
        {
            await _original.DisposeAsync().AsTask().WaitAsync(Timeout);
        }
        else
        {
            Manager.Stop();
        }
        Assert.Equal(0, Manager.OutboundSenderCount);
    }

    internal void RegisterReplacement(int id)
    {
        var source = new MemoryStream();
        _replacementSources.Add(source);
        _replacements.Add(Register(RpcStreamAttachment.FromStream(new(id, RpcStreamKind.Binary), source)));
    }

    internal async Task FinishOriginalAsync()
    {
        _source.Release.TrySetResult(0);
        await _pumps.WaitAsync(Timeout);
        await _original!.DisposeAsync().AsTask().WaitAsync(Timeout);
    }

    internal void AddCredit(int id)
    {
        using var credit = RpcRawFrame.FrameInt32(id, MessageType.StreamCredit, 1);
        Assert.True(Manager.TryAddCredit(credit));
    }

    internal async Task AssertReplacementSendsAsync(int id)
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await Manager.SendStreamItemAsync(id, new byte[] { 4, 2 }, cancellation.Token);
        Assert.Equal(new byte[] { 4, 2 }, Assert.Single(SentItems));
    }

    public async ValueTask DisposeAsync()
    {
        _source.Release.TrySetResult(0);
        if (_original is not null)
        {
            await _original.DisposeAsync();
        }
        await _pumps.WaitAsync(Timeout);
        foreach (var replacement in _replacements)
        {
            await replacement.DisposeAsync();
        }
        Manager.Stop();
        await _source.DisposeAsync();
        foreach (var source in _replacementSources)
        {
            await source.DisposeAsync();
        }
    }

    private RpcOutboundStreamSet Register(RpcStreamAttachment attachment) => _batch
        ? Manager.RegisterOutbound([attachment], CancellationToken.None)
        : Manager.RegisterOutbound(attachment, CancellationToken.None);

    private Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Assert.True(MessageFramer.TryReadFrameHeader(frame, out _, out var type));
        if (type == MessageType.StreamItem)
        {
            SentItems.Add(frame.Slice(MessageFramer.HeaderSize).ToArray());
        }
        return Task.CompletedTask;
    }

    private sealed class PendingStream : MemoryStream
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<int> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return new ValueTask<int>(Release.Task);
        }
    }
}
