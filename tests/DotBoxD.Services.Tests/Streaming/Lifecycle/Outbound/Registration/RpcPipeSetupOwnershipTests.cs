using System.IO.Pipelines;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Client;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound.Registration;

public sealed class RpcPipeSetupOwnershipTests
{
    [Theory]
    [InlineData("Duplicate", false)]
    [InlineData("Duplicate", true)]
    [InlineData("InvalidTarget", false)]
    [InlineData("InvalidTarget", true)]
    [InlineData("PreCanceled", false)]
    [InlineData("PreCanceled", true)]
    public async Task Failed_call_does_not_complete_a_reader_owned_by_another_registration(string failure, bool batch)
    {
        var (manager, invoker) = CreateInvoker();
        var pipe = new Pipe();
        var attachment = RpcStreamAttachment.FromPipe(manager.ReserveOutbound(RpcStreamKind.Binary), pipe, completeReader: true);
        var owner = manager.RegisterOutbound(attachment, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        try
        {
            if (failure == "PreCanceled")
            {
                cancellation.Cancel();
            }
            var error = await Record.ExceptionAsync(() => Invoke(invoker, attachment, batch,
                failure == "InvalidTarget" ? "" : "Receiver", cancellation.Token));

            Assert.IsType(failure switch
            {
                "Duplicate" => typeof(ServiceProtocolException),
                "InvalidTarget" => typeof(ArgumentException),
                _ => typeof(OperationCanceledException),
            }, error);
            Assert.False((await pipe.Writer.FlushAsync()).IsCompleted);
            Assert.Equal(1, manager.OutboundSenderCount);
        }
        finally
        {
            await owner.DisposeAsync();
            await pipe.Reader.CompleteAsync();
            await pipe.Writer.CompleteAsync();
            manager.Stop();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Failed_call_completes_only_unregistered_owned_readers(bool batch, bool owned)
    {
        var (manager, invoker) = CreateInvoker();
        var pipe = new Pipe();
        var attachment = RpcStreamAttachment.FromPipe(manager.ReserveOutbound(RpcStreamKind.Binary), pipe, completeReader: owned);
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => Invoke(invoker, attachment, batch, "", CancellationToken.None));

            Assert.Equal(owned, (await pipe.Writer.FlushAsync()).IsCompleted);
        }
        finally
        {
            await pipe.Reader.CompleteAsync();
            await pipe.Writer.CompleteAsync();
            manager.Stop();
        }
    }

    private static Task<int> Invoke(RpcPeerOutboundInvoker invoker, RpcStreamAttachment attachment,
        bool batch, string service, CancellationToken ct) => batch
        ? invoker.InvokeAsync<RpcStreamHandle, int>(service, "Read", attachment.Handle, [attachment], ct)
        : invoker.InvokeAsync<RpcStreamHandle, int>(service, "Read", attachment.Handle, attachment, ct);

    private static (RpcStreamManager Manager, RpcPeerOutboundInvoker Invoker) CreateInvoker()
    {
        var serializer = new MessagePackRpcSerializer();
        var manager = new RpcStreamManager(serializer, static (_, _) => Task.CompletedTask, null);
        var invoker = new RpcPeerOutboundInvoker(serializer, new RpcPeerOptions(), static () => { },
            static (_, _) => Task.CompletedTask, manager);
        return (manager, invoker);
    }
}
