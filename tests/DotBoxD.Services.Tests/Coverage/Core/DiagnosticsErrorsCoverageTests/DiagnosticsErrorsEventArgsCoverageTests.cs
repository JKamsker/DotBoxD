using DotBoxD.Services.Attributes;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using Xunit;
namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class DiagnosticsErrorsEventArgsCoverageTests
{
    [Fact]
    public void RpcMethodAttribute_AppliedToMethod_IsDiscoverableViaReflection()
    {
        var method = typeof(IDecoratedService).GetMethod(nameof(IDecoratedService.RenamedAsync))!;
        var attribute = method.GetCustomAttributes(typeof(RpcMethodAttribute), false)
            .Cast<RpcMethodAttribute>()
            .Single();

        Assert.Equal("WireMethod", attribute.Name);
    }

}

/// <summary>
/// Construction and property coverage for the public peer/host event-args records.
/// </summary>
public sealed class EventArgsCoverageTests
{
    [Fact]
    public void RpcDisconnectedEventArgs_GracefulClose_HasNullError()
    {
        var args = new RpcDisconnectedEventArgs("tcp://1.2.3.4:9000", error: null);

        Assert.Equal("tcp://1.2.3.4:9000", args.RemoteEndpoint);
        Assert.Null(args.Error);
    }

    [Fact]
    public void RpcDisconnectedEventArgs_WithError_ExposesError()
    {
        var error = new IOException("reset");
        var args = new RpcDisconnectedEventArgs("ep", error);

        Assert.Same(error, args.Error);
    }

    [Fact]
    public void RpcDisconnectedEventArgs_RejectsNullRemoteEndpoint()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new RpcDisconnectedEventArgs(null!, error: null));

        Assert.Equal("remoteEndpoint", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void RpcDisconnectedEventArgs_RejectsBlankRemoteEndpoint(string remoteEndpoint)
    {
        DiagnosticAssert.Argument(() => new RpcDisconnectedEventArgs(remoteEndpoint, error: null), "remoteEndpoint");
    }

    [Fact]
    public void RpcReadErrorEventArgs_ExposesEndpointAndError()
    {
        var error = new InvalidOperationException("read failed");
        var args = new RpcReadErrorEventArgs("ep", error);

        Assert.Equal("ep", args.RemoteEndpoint);
        Assert.Same(error, args.Error);
    }

    [Fact]
    public void RpcReadErrorEventArgs_RejectsNullConstructorArguments()
    {
        DiagnosticAssert.ArgumentNull(() => new RpcReadErrorEventArgs(null!, new Exception("read")), "remoteEndpoint");
        DiagnosticAssert.ArgumentNull(() => new RpcReadErrorEventArgs("ep", null!), "error");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void RpcReadErrorEventArgs_RejectsBlankRemoteEndpoint(string remoteEndpoint)
    {
        DiagnosticAssert.Argument(() => new RpcReadErrorEventArgs(remoteEndpoint, new Exception("read")), "remoteEndpoint");
    }

    [Fact]
    public void RpcProtocolErrorEventArgs_MinimalConstructor_LeavesErrorNull()
    {
        var args = new RpcProtocolErrorEventArgs("ep", messageId: 7, MessageType.Request, "bad header");

        Assert.Equal("ep", args.RemoteEndpoint);
        Assert.Equal(7, args.MessageId);
        Assert.Equal(MessageType.Request, args.MessageType);
        Assert.Equal("bad header", args.Message);
        Assert.Null(args.Error);
    }

    [Fact]
    public void RpcProtocolErrorEventArgs_FullConstructor_ExposesError()
    {
        var error = new ServiceProtocolException("decode failed");
        var args = new RpcProtocolErrorEventArgs("ep", 9, MessageType.Response, "decode failed", error);

        Assert.Equal(9, args.MessageId);
        Assert.Equal(MessageType.Response, args.MessageType);
        Assert.Same(error, args.Error);
    }

    [Fact]
    public void RpcProtocolErrorEventArgs_RejectsNullConstructorArguments()
    {
        DiagnosticAssert.ArgumentNull(
            () => new RpcProtocolErrorEventArgs(null!, 7, MessageType.Request, "bad header"),
            "remoteEndpoint");
        DiagnosticAssert.ArgumentNull(
            () => new RpcProtocolErrorEventArgs("ep", 7, MessageType.Request, null!),
            "message");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void RpcProtocolErrorEventArgs_RejectsBlankRemoteEndpoint(string remoteEndpoint)
    {
        DiagnosticAssert.Argument(
            () => new RpcProtocolErrorEventArgs(remoteEndpoint, 7, MessageType.Request, "bad header"),
            "remoteEndpoint");
        DiagnosticAssert.Argument(
            () => new RpcProtocolErrorEventArgs(remoteEndpoint, 7, MessageType.Request, "bad header", new Exception("decode")),
            "remoteEndpoint");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void RpcProtocolErrorEventArgs_RejectsBlankMessage(string message)
    {
        DiagnosticAssert.Argument(
            () => new RpcProtocolErrorEventArgs("ep", 7, MessageType.Request, message),
            "message");
        DiagnosticAssert.Argument(
            () => new RpcProtocolErrorEventArgs("ep", 7, MessageType.Request, message, new Exception("decode")),
            "message");
    }

    [Fact]
    public void RpcDispatchErrorEventArgs_ExposesAllRequestCoordinates()
    {
        var error = new InvalidOperationException("dispatch failed");
        var args = new RpcDispatchErrorEventArgs(
            "ep",
            messageId: 42,
            serviceName: "Game",
            methodName: "Move",
            instanceId: "inst-7",
            error);

        Assert.Equal("ep", args.RemoteEndpoint);
        Assert.Equal(42, args.MessageId);
        Assert.Equal("Game", args.ServiceName);
        Assert.Equal("Move", args.MethodName);
        Assert.Equal("inst-7", args.InstanceId);
        Assert.Same(error, args.Error);
    }

    [Fact]
    public void RpcDispatchErrorEventArgs_NullInstanceId_IsAllowed()
    {
        var args = new RpcDispatchErrorEventArgs(
            "ep", 1, "Game", "Status", instanceId: null, new Exception("x"));

        Assert.Null(args.InstanceId);
    }

    [Fact]
    public void RpcDispatchErrorEventArgs_RejectsNullConstructorArguments()
    {
        DiagnosticAssert.ArgumentNull(
            () => new RpcDispatchErrorEventArgs(null!, 1, "Game", "Status", null, new Exception("x")),
            "remoteEndpoint");
        DiagnosticAssert.ArgumentNull(
            () => new RpcDispatchErrorEventArgs("ep", 1, null!, "Status", null, new Exception("x")),
            "serviceName");
        DiagnosticAssert.ArgumentNull(
            () => new RpcDispatchErrorEventArgs("ep", 1, "Game", null!, null, new Exception("x")),
            "methodName");
        DiagnosticAssert.ArgumentNull(
            () => new RpcDispatchErrorEventArgs("ep", 1, "Game", "Status", null, null!),
            "error");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void RpcDispatchErrorEventArgs_RejectsBlankRemoteEndpoint(string remoteEndpoint)
    {
        DiagnosticAssert.Argument(
            () => new RpcDispatchErrorEventArgs(remoteEndpoint, 1, "Game", "Status", null, new Exception("x")),
            "remoteEndpoint");
    }

    [Theory]
    [InlineData("", "Move", "serviceName")]
    [InlineData("   ", "Move", "serviceName")]
    [InlineData("\t", "Move", "serviceName")]
    [InlineData("Game", "", "methodName")]
    [InlineData("Game", "   ", "methodName")]
    [InlineData("Game", "\t", "methodName")]
    public void RpcDispatchErrorEventArgs_RejectsBlankRequestNames(
        string serviceName,
        string methodName,
        string paramName)
    {
        DiagnosticAssert.Argument(
            () => new RpcDispatchErrorEventArgs("ep", 1, serviceName, methodName, null, new Exception("x")),
            paramName);
    }

    [Fact]
    public void RpcHostErrorEventArgs_ExposesError()
    {
        var error = new InvalidOperationException("accept failed");
        var args = new RpcHostErrorEventArgs(error);

        Assert.Same(error, args.Error);
    }

    [Fact]
    public void RpcHostErrorEventArgs_RejectsNullException()
    {
        DiagnosticAssert.ArgumentNull(() => new RpcHostErrorEventArgs(null!), "exception");
    }

    [Fact]
    public void RpcPeerEventArgs_RejectsNullPeer()
    {
        DiagnosticAssert.ArgumentNull(() => new RpcPeerEventArgs(null!), "peer");
    }

}
