using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class RpcHostReentrantDisposalRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ForEachPeerCallback_DisposeAsyncCompletesOrFailsClosedInsteadOfDeadlocking()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var _ = clientConnection;
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackTerminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RpcHost? host = null;

        host = RpcHost
            .Listen(new SingleConnectionServerTransport(serverConnection), new MessagePackRpcSerializer())
            .ForEachPeer(_ =>
            {
                callbackEntered.TrySetResult();
                try
                {
                    host!.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    callbackTerminal.TrySetResult();
                }
                catch (Exception ex)
                {
                    callbackTerminal.TrySetException(ex);
                }
            });

        await host.StartAsync();
        await callbackEntered.Task.WaitAsync(Timeout);

        await callbackTerminal.Task.WaitAsync(Timeout);
    }
}
