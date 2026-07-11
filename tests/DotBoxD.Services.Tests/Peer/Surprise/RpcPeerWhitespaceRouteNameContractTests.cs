using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Peer.Surprise;

public sealed class RpcPeerWhitespaceRouteNameContractTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static TheoryData<
        string,
        string,
        string,
        string,
        Func<RpcPeer, string, string, Task>> RpcPeerWhitespaceRouteCalls()
    {
        var data = new TheoryData<string, string, string, string, Func<RpcPeer, string, string, Task>>();
        AddRpcPeerCases(data, "whitespace service", "   ", "Method", "service");
        AddRpcPeerCases(data, "tab method", "Service", "\t", "method");
        return data;
    }

    public static TheoryData<
        string,
        string,
        string,
        string,
        Func<IRpcInvoker, string, string, Task>> InvokerWhitespaceRouteCalls()
    {
        var data = new TheoryData<string, string, string, string, Func<IRpcInvoker, string, string, Task>>();
        AddInvokerCases(data, "whitespace service", "   ", "Method", "service");
        AddInvokerCases(data, "tab method", "Service", "\t", "method");
        return data;
    }

    [Theory]
    [MemberData(nameof(RpcPeerWhitespaceRouteCalls))]
    public async Task RpcPeer_InvokeAsync_rejects_whitespace_route_names_before_starting(
        string scenario,
        string service,
        string method,
        string expectedParamName,
        Func<RpcPeer, string, string, Task> invoke)
    {
        Assert.False(string.IsNullOrWhiteSpace(scenario));
        await using var context = CreateUnstartedPeer();

        var exception = await Record.ExceptionAsync(
            () => invoke(context.Peer, service, method).WaitAsync(Timeout));
        var provideException = Record.Exception(
            () => context.Peer.Provide((IServiceDispatcher)new NoopDispatcher()));

        AssertRejectedAtLocalRouteBoundary(exception, expectedParamName, context.Channel.SendCount, provideException);
    }

    [Theory]
    [MemberData(nameof(InvokerWhitespaceRouteCalls))]
    public async Task IRpcInvoker_InvokeAsync_rejects_whitespace_route_names_before_starting(
        string scenario,
        string service,
        string method,
        string expectedParamName,
        Func<IRpcInvoker, string, string, Task> invoke)
    {
        Assert.False(string.IsNullOrWhiteSpace(scenario));
        await using var context = CreateUnstartedPeer();

        var exception = await Record.ExceptionAsync(
            () => invoke((IRpcInvoker)context.Peer, service, method).WaitAsync(Timeout));
        var provideException = Record.Exception(
            () => context.Peer.Provide((IServiceDispatcher)new NoopDispatcher()));

        AssertRejectedAtLocalRouteBoundary(exception, expectedParamName, context.Channel.SendCount, provideException);
    }

    private static void AddRpcPeerCases(
        TheoryData<string, string, string, string, Func<RpcPeer, string, string, Task>> data,
        string suffix,
        string service,
        string method,
        string expectedParamName)
    {
        data.Add(
            "RpcPeer response " + suffix,
            service,
            method,
            expectedParamName,
            (peer, currentService, currentMethod) => peer.InvokeAsync<int>(currentService, currentMethod));
        data.Add(
            "RpcPeer request-response " + suffix,
            service,
            method,
            expectedParamName,
            (peer, currentService, currentMethod) =>
                peer.InvokeAsync<int, int>(currentService, currentMethod, 1));
        data.Add(
            "RpcPeer request-no-response " + suffix,
            service,
            method,
            expectedParamName,
            (peer, currentService, currentMethod) => peer.InvokeAsync(currentService, currentMethod));
        data.Add(
            "RpcPeer instance response " + suffix,
            service,
            method,
            expectedParamName,
            (peer, currentService, currentMethod) =>
                peer.InvokeOnInstanceAsync<int>(currentService, "instance-1", currentMethod));
    }

    private static void AddInvokerCases(
        TheoryData<string, string, string, string, Func<IRpcInvoker, string, string, Task>> data,
        string suffix,
        string service,
        string method,
        string expectedParamName)
    {
        data.Add(
            "IRpcInvoker response " + suffix,
            service,
            method,
            expectedParamName,
            (invoker, currentService, currentMethod) => invoker.InvokeAsync<int>(currentService, currentMethod));
        data.Add(
            "IRpcInvoker request-response " + suffix,
            service,
            method,
            expectedParamName,
            (invoker, currentService, currentMethod) =>
                invoker.InvokeAsync<int, int>(currentService, currentMethod, 1));
        data.Add(
            "IRpcInvoker request-no-response " + suffix,
            service,
            method,
            expectedParamName,
            (invoker, currentService, currentMethod) => invoker.InvokeAsync(currentService, currentMethod));
        data.Add(
            "IRpcInvoker instance response " + suffix,
            service,
            method,
            expectedParamName,
            (invoker, currentService, currentMethod) =>
                invoker.InvokeOnInstanceAsync<int>(currentService, "instance-1", currentMethod));
    }

    private static void AssertRejectedAtLocalRouteBoundary(
        Exception? exception,
        string expectedParamName,
        int sendCount,
        Exception? provideException)
    {
        var failures = new List<string>();
        if (exception is not ArgumentException argumentException)
        {
            failures.Add(
                "Expected local ArgumentException, got " +
                (exception is null ? "no exception" : exception.GetType().FullName) +
                ".");
        }
        else if (!string.Equals(argumentException.ParamName, expectedParamName, StringComparison.Ordinal))
        {
            failures.Add(
                $"Expected ParamName '{expectedParamName}', got '{argumentException.ParamName}'.");
        }

        if (sendCount != 0)
        {
            failures.Add($"Expected no channel send before route validation, got {sendCount} send(s).");
        }

        if (provideException is not null)
        {
            failures.Add(
                "Expected the peer to remain unstarted so Provide succeeds, got " +
                provideException.GetType().FullName +
                ".");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static PeerContext CreateUnstartedPeer()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var channel = new CountingChannel(serverConnection);
        var peer = RpcPeer.Over(
            channel,
            new MessagePackRpcSerializer(),
            new RpcPeerOptions { RequestTimeout = Timeout });

        return new PeerContext(peer, clientConnection, channel);
    }

    private sealed class PeerContext : IAsyncDisposable
    {
        private readonly IRpcChannel _otherSide;

        public PeerContext(RpcPeer peer, IRpcChannel otherSide, CountingChannel channel)
        {
            Peer = peer;
            _otherSide = otherSide;
            Channel = channel;
        }

        public RpcPeer Peer { get; }

        public CountingChannel Channel { get; }

        public async ValueTask DisposeAsync()
        {
            await Peer.DisposeAsync().ConfigureAwait(false);
            await _otherSide.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class CountingChannel : IRpcChannel
    {
        private readonly IRpcChannel _inner;
        private int _sendCount;

        public CountingChannel(IRpcChannel inner) => _inner = inner;

        public bool IsConnected => _inner.IsConnected;

        public string RemoteEndpoint => _inner.RemoteEndpoint;

        public int SendCount => Volatile.Read(ref _sendCount);

        public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _sendCount);
            await _inner.SendAsync(data, ct).ConfigureAwait(false);
        }

        public Task<Payload> ReceiveAsync(CancellationToken ct = default) =>
            _inner.ReceiveAsync(ct);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private sealed class NoopDispatcher : IServiceDispatcher
    {
        public string ServiceName => "Noop";

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) => Task.CompletedTask;
    }
}
