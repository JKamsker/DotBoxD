using System.Reflection;
using System.Runtime.ExceptionServices;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using FluentAssertions;
using static DotBoxD.Services.SourceGenerator.Tests.SubServices.NestedServiceTestCompiler;

namespace DotBoxD.Services.SourceGenerator.Tests.SubServices;

public sealed class SubServiceHandleValidationRuntimeTests
{
    private const string Source = """
        using DotBoxD.Services.Attributes;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Reviewed.SubServiceHandleValidation
        {
            [RpcService(Name = "sub-custom")]
            public interface ISub
            {
                Task<int> CountAsync(int value, CancellationToken ct = default);
            }

            [RpcService(Name = "root-custom")]
            public interface IRoot
            {
                Task<ISub> OpenAsync(CancellationToken ct = default);
            }
        }
        """;

    [Theory]
    [InlineData("wrong-sub")]
    [InlineData("")]
    public async Task RootProxy_RejectsMalformedSubServiceHandleServiceName(string serviceName)
    {
        var (assembly, _) = Compile(Source);
        var client = new RecordingInvoker
        {
            HandleResult = new ServiceHandle { ServiceName = serviceName, InstanceId = "sub-1" },
        };
        var rootProxy = CreateRootProxy(assembly, client);

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await InvokeRootOpenAsync(rootProxy, CancellationToken.None));

        exception.Message.Should().ContainAny("ServiceHandle.ServiceName", "sub-custom");
        client.LastInstanceService.Should().BeNull(
            "malformed handles should fail before a generated sub-service proxy can issue instance calls");
    }

    [Fact]
    public async Task RootProxy_AllowsMatchingSubServiceHandleServiceName()
    {
        var (assembly, _) = Compile(Source);
        var client = new RecordingInvoker
        {
            HandleResult = new ServiceHandle { ServiceName = "sub-custom", InstanceId = "sub-1" },
            CountResult = 42,
        };
        var rootProxy = CreateRootProxy(assembly, client);

        using var rootCts = new CancellationTokenSource();
        var sub = await InvokeRootOpenAsync(rootProxy, rootCts.Token);
        using var cts = new CancellationTokenSource();
        var count = sub.GetType().GetMethod("CountAsync", new[] { typeof(int), typeof(CancellationToken) })!;
        var result = await (Task<int>)count.Invoke(sub, new object[] { 7, cts.Token })!;

        result.Should().Be(42);
        client.LastRootService.Should().Be("root-custom");
        client.LastRootMethod.Should().Be("OpenAsync");
        client.LastRootCancellationToken.Should().Be(rootCts.Token);
        client.LastInstanceService.Should().Be("sub-custom");
        client.LastInstanceId.Should().Be("sub-1");
        client.LastInstanceMethod.Should().Be("CountAsync");
        client.LastInstanceRequest.Should().Be(7);
        client.LastInstanceCancellationToken.Should().Be(cts.Token);
    }

    private static object CreateRootProxy(Assembly assembly, IRpcInvoker client)
        => Activator.CreateInstance(
            assembly.GetType("Reviewed.SubServiceHandleValidation.RootProxy")!,
            client)!;

    private static async Task<object> InvokeRootOpenAsync(object rootProxy, CancellationToken ct)
    {
        var open = rootProxy.GetType().GetMethod("OpenAsync", new[] { typeof(CancellationToken) })!;
        object task;
        try
        {
            task = open.Invoke(rootProxy, new object[] { ct })!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        await (Task)task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private sealed class RecordingInvoker : IRpcInvoker
    {
        public ServiceHandle HandleResult { get; init; }
        public int CountResult { get; init; }
        public string? LastRootService { get; private set; }
        public string? LastRootMethod { get; private set; }
        public CancellationToken LastRootCancellationToken { get; private set; }
        public string? LastInstanceService { get; private set; }
        public string? LastInstanceId { get; private set; }
        public string? LastInstanceMethod { get; private set; }
        public object? LastInstanceRequest { get; private set; }
        public CancellationToken LastInstanceCancellationToken { get; private set; }

        public Task<TResponse> InvokeAsync<TResponse>(
            string service,
            string method,
            CancellationToken ct = default)
        {
            LastRootService = service;
            LastRootMethod = method;
            LastRootCancellationToken = ct;
            return Task.FromResult((TResponse)(object)HandleResult);
        }

        public Task<TResponse> InvokeAsync<TRequest, TResponse>(
            string service,
            string method,
            TRequest request,
            CancellationToken ct = default)
            => Task.FromResult(default(TResponse)!);

        public Task InvokeAsync<TRequest>(
            string service,
            string method,
            TRequest request,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task InvokeAsync(string service, string method, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
            string service,
            string instanceId,
            string method,
            TRequest request,
            CancellationToken ct = default)
        {
            LastInstanceService = service;
            LastInstanceId = instanceId;
            LastInstanceMethod = method;
            LastInstanceRequest = request;
            LastInstanceCancellationToken = ct;
            return Task.FromResult((TResponse)(object)CountResult);
        }

        public Task<TResponse> InvokeOnInstanceAsync<TResponse>(
            string service,
            string instanceId,
            string method,
            CancellationToken ct = default)
            => Task.FromResult(default(TResponse)!);

        public Task InvokeOnInstanceAsync<TRequest>(
            string service,
            string instanceId,
            string method,
            TRequest request,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task InvokeOnInstanceAsync(
            string service,
            string instanceId,
            string method,
            CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
