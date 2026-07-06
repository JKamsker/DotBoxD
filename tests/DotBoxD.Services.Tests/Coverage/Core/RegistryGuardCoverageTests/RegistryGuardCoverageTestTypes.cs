using System.Buffers;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;

namespace DotBoxD.Services.Tests.Coverage.Core;

// Throwaway service surfaces; no generator runs for these tests.
internal interface IDefaultMetadataService
{
    Task DoAsync(CancellationToken ct = default);
}

internal interface IValidationProbeService
{
    Task DoAsync(CancellationToken ct = default);
}

internal interface INullProxyFactoryService
{
    Task DoAsync(CancellationToken ct = default);
}

internal interface INullDispatcherFactoryService
{
    Task DoAsync(CancellationToken ct = default);
}

internal sealed class ConcreteRegisteredService
{
}

internal sealed class DefaultMetadataImpl : IDefaultMetadataService
{
    public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class DefaultMetadataProxy : IDefaultMetadataService
{
    public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class ProbeProxy : IValidationProbeService
{
    public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class NullDispatcherFactoryImpl : INullDispatcherFactoryService
{
    public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class NullDispatcherFactoryProxy : INullDispatcherFactoryService
{
    public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class DefaultMetadataDispatcher : IServiceDispatcher
{
    public string ServiceName => nameof(IDefaultMetadataService);

    public Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class NullProxyFactoryDispatcher : IServiceDispatcher
{
    public string ServiceName => nameof(INullProxyFactoryService);

    public Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class ProbeDispatcher : IServiceDispatcher
{
    public string ServiceName => "Probe";

    public Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>An <see cref="IRpcInvoker"/> that is never actually invoked by these tests.</summary>
internal sealed class NoopInvoker : IRpcInvoker
{
    public Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service, string method, TRequest request, CancellationToken ct = default) =>
        Task.FromResult(default(TResponse)!);

    public Task<TResponse> InvokeAsync<TResponse>(string service, string method, CancellationToken ct = default) =>
        Task.FromResult(default(TResponse)!);

    public Task InvokeAsync<TRequest>(string service, string method, TRequest request, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task InvokeAsync(string service, string method, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service, string instanceId, string method, TRequest request, CancellationToken ct = default) =>
        Task.FromResult(default(TResponse)!);

    public Task<TResponse> InvokeOnInstanceAsync<TResponse>(
        string service, string instanceId, string method, CancellationToken ct = default) =>
        Task.FromResult(default(TResponse)!);

    public Task InvokeOnInstanceAsync<TRequest>(
        string service, string instanceId, string method, TRequest request, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task InvokeOnInstanceAsync(
        string service, string instanceId, string method, CancellationToken ct = default) =>
        Task.CompletedTask;
}
