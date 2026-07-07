using System.Buffers;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using Shared;

namespace DotBoxD.Services.Tests.Coverage.Core;

internal static class RegistryDiagnosticsTestSupport
{
    internal static GeneratedService ValidCustomService() =>
        new(
            typeof(ICustomRegisteredService),
            typeof(CustomProxy),
            typeof(CustomDispatcher),
            "Custom");
}

internal interface IUngeneratedCoverageService
{
    Task PingAsync(CancellationToken ct = default);
}

public interface ICustomRegisteredService
{
    Task DoAsync(CancellationToken ct = default);
}

public interface IReplaceableService
{
    Task DoAsync(CancellationToken ct = default);
}

internal sealed class CustomImplementation : ICustomRegisteredService
{
    public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class CustomProxy : ICustomRegisteredService
{
    public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class ReplaceableProxyV1 : IReplaceableService
{
    public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class ReplaceableProxyV2 : IReplaceableService
{
    public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class CustomDispatcher : IServiceDispatcher
{
    public string ServiceName => "Custom";

    public Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class RecordingServiceSink : IRpcServiceRegistrationSink
{
    public List<Type> ServiceTypes { get; } = new();

    public void AddService<TService, TImplementation>()
        where TService : class
        where TImplementation : TService =>
        ServiceTypes.Add(typeof(TService));
}

internal sealed class RecordingGeneratedSink : IRpcGeneratedServiceRegistrationSink
{
    public List<Type> ServiceTypes { get; } = new();

    public void AddService<TService, TProxy, TDispatcher>()
        where TService : class
        where TProxy : TService
        where TDispatcher : IServiceDispatcher =>
        ServiceTypes.Add(typeof(TService));
}

internal sealed class RecordingInvoker : IRpcInvoker
{
    public string? LastService { get; private set; }

    public Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        LastService = service;
        return Task.FromResult(CannedResponse<TResponse>());
    }

    public Task<TResponse> InvokeAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default)
    {
        LastService = service;
        return Task.FromResult(CannedResponse<TResponse>());
    }

    public Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        LastService = service;
        return Task.CompletedTask;
    }

    public Task InvokeAsync(string service, string method, CancellationToken ct = default)
    {
        LastService = service;
        return Task.CompletedTask;
    }

    public Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        LastService = service;
        return Task.FromResult(CannedResponse<TResponse>());
    }

    public Task<TResponse> InvokeOnInstanceAsync<TResponse>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        LastService = service;
        return Task.FromResult(CannedResponse<TResponse>());
    }

    public Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        LastService = service;
        return Task.CompletedTask;
    }

    public Task InvokeOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        LastService = service;
        return Task.CompletedTask;
    }

    private static TResponse CannedResponse<TResponse>()
    {
        if (typeof(TResponse) == typeof(ServerStatus))
        {
            return (TResponse)(object)new ServerStatus
            {
                PlayerCount = 0,
                ServerTime = "now",
                Version = "from-invoker",
            };
        }

        return default!;
    }
}
