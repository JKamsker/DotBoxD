using System.Buffers;
using DotBoxD.Services.Attributes;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Server;
using Xunit;
namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class DiagnosticsErrorsExceptionCoverageTests
{
    [Fact]
    public async Task IServiceDispatcher_DispatchOnInstanceAsync_DefaultMember_ThrowsNotFound()
    {
        // A dispatcher that does not override the instance-scoped entry point falls through to the
        // interface default implementation, which must reject the call as not-found.
        IServiceDispatcher dispatcher = new RootOnlyDispatcher();
        var output = new ArrayBufferWriter<byte>();

        var ex = await Assert.ThrowsAsync<ServiceNotFoundException>(() =>
            dispatcher.DispatchOnInstanceAsync(
                "instance-1",
                "Method",
                ReadOnlyMemory<byte>.Empty,
                new ThrowingSerializer(),
                new InstanceRegistry(),
                output));

        Assert.Contains("RootOnly", ex.Message);
        Assert.Contains("instance-scoped dispatch", ex.Message);
    }

}

/// <summary>
/// Direct construction coverage for the public error/exception types and the wire error-type
/// constants. These are simple value carriers; the assertions pin their public contract.
/// </summary>
public sealed class RpcErrorTypesCoverageTests
{
    [Fact]
    public void RpcErrorInfo_FromException_CopiesMessageAndRuntimeTypeName()
    {
        var error = new InvalidOperationException("boom");

        var info = RpcErrorInfo.FromException(error);

        Assert.Equal("boom", info.Message);
        Assert.Equal(nameof(InvalidOperationException), info.Type);
    }

    [Fact]
    public void RpcErrorInfo_FromException_RejectsNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => RpcErrorInfo.FromException(null!));

        Assert.Equal("exception", ex.ParamName);
    }

    [Fact]
    public void RpcErrorInfo_Constructor_StoresMessageAndType()
    {
        var info = new RpcErrorInfo("safe message", "AppError");

        Assert.Equal("safe message", info.Message);
        Assert.Equal("AppError", info.Type);
    }

    [Fact]
    public void RpcErrorInfo_ValueEquality_HoldsForSameContent()
    {
        var a = new RpcErrorInfo("m", "t");
        var b = new RpcErrorInfo("m", "t");
        var c = new RpcErrorInfo("m", "other");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void RpcErrorTypes_DefinesDistinctNonEmptyWireNames()
    {
        var names = new[]
        {
            RpcErrorTypes.InternalError,
            RpcErrorTypes.InboundRejected,
            RpcErrorTypes.ServiceNotFound,
            RpcErrorTypes.MethodNotFound,
            RpcErrorTypes.InstanceNotFound,
            RpcErrorTypes.QueueFull,
            RpcErrorTypes.ProtocolError,
        };

        Assert.All(names, n => Assert.False(string.IsNullOrEmpty(n)));
        Assert.Equal(names.Length, names.Distinct().Count());
        Assert.Equal("RpcInternalError", RpcErrorTypes.InternalError);
        Assert.Equal("RpcServiceNotFound", RpcErrorTypes.ServiceNotFound);
    }
}

/// <summary>
/// Construction and property coverage for the DotBoxD exception hierarchy.
/// </summary>
public sealed class ServiceExceptionCoverageTests
{
    [Fact]
    public void ServiceException_Parameterless_HasNoCustomMessageAndIsException()
    {
        var ex = new ServiceException();

        Assert.IsAssignableFrom<Exception>(ex);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void ServiceException_WithMessage_StoresMessage()
    {
        var ex = new ServiceException("explained");

        Assert.Equal("explained", ex.Message);
    }

    [Fact]
    public void ServiceException_WithInnerException_StoresBoth()
    {
        var inner = new InvalidOperationException("root cause");
        var ex = new ServiceException("wrapper", inner);

        Assert.Equal("wrapper", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void RemoteServiceException_ExposesRemoteType()
    {
        var ex = new RemoteServiceException("remote failed", "AppCustomError");

        Assert.Equal("remote failed", ex.Message);
        Assert.Equal("AppCustomError", ex.RemoteExceptionType);
        Assert.IsAssignableFrom<ServiceException>(ex);
    }

    [Fact]
    public void ServiceConnectionException_WithInner_StoresBoth()
    {
        var inner = new IOException("socket gone");
        var ex = new ServiceConnectionException("connection lost", inner);

        Assert.Equal("connection lost", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void ServiceTimeoutException_StoresMessage()
    {
        var ex = new ServiceTimeoutException("timed out");

        Assert.Equal("timed out", ex.Message);
        Assert.IsAssignableFrom<ServiceException>(ex);
    }

    [Fact]
    public void ServiceProtocolException_StoresMessage()
    {
        var ex = new ServiceProtocolException("bad frame");

        Assert.Equal("bad frame", ex.Message);
        Assert.IsAssignableFrom<ServiceException>(ex);
    }

    [Fact]
    public void ServiceNotFoundException_DefaultKind_IsService()
    {
        var ex = new ServiceNotFoundException("no service");

        Assert.Equal("no service", ex.Message);
        Assert.Equal(ServiceNotFoundException.NotFoundKind.Service, ex.Kind);
    }

    [Theory]
    [InlineData(ServiceNotFoundException.NotFoundKind.Service)]
    [InlineData(ServiceNotFoundException.NotFoundKind.Method)]
    [InlineData(ServiceNotFoundException.NotFoundKind.Instance)]
    public void ServiceNotFoundException_WithKind_StoresKind(ServiceNotFoundException.NotFoundKind kind)
    {
        var ex = new ServiceNotFoundException("missing", kind);

        Assert.Equal(kind, ex.Kind);
        Assert.Equal("missing", ex.Message);
    }

    [Fact]
    public void ServiceNotFoundException_WithUndefinedKind_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceNotFoundException("missing", (ServiceNotFoundException.NotFoundKind)99));

        Assert.Equal("kind", ex.ParamName);
    }
}

/// <summary>
/// Coverage for the RPC marker attributes and their optional custom-name properties.
/// </summary>
public sealed class AttributeCoverageTests
{
    [Fact]
    public void RpcServiceAttribute_DefaultName_IsNull()
    {
        var attribute = new RpcServiceAttribute();

        Assert.Null(attribute.Name);
    }

    [Fact]
    public void RpcServiceAttribute_WithCustomName_ExposesName()
    {
        var attribute = new RpcServiceAttribute { Name = "Custom.Service" };

        Assert.Equal("Custom.Service", attribute.Name);
    }

    [Fact]
    public void RpcServiceAttribute_AppliedToInterface_IsDiscoverableViaReflection()
    {
        var attribute = typeof(IDecoratedService).GetCustomAttributes(typeof(RpcServiceAttribute), false)
            .Cast<RpcServiceAttribute>()
            .Single();

        Assert.Equal("decorated-wire", attribute.Name);
    }

    [Fact]
    public void RpcMethodAttribute_DefaultName_IsNull()
    {
        var attribute = new RpcMethodAttribute();

        Assert.Null(attribute.Name);
    }

    [Fact]
    public void RpcMethodAttribute_WithCustomName_ExposesName()
    {
        var attribute = new RpcMethodAttribute { Name = "CustomMethod" };

        Assert.Equal("CustomMethod", attribute.Name);
    }

}
