using System.Buffers;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using Shared;
using Xunit;
namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed partial class GeneratedServiceRegistryCoverageTests
{
    [Fact]
    public void Register_MetadataDescribingWrongServiceType_ThrowsArgumentException()
    {
        // ValidateService must reject metadata whose ServiceType disagrees with TService.
        var mismatched = new GeneratedService(
            typeof(IGameService),
            typeof(CustomProxy),
            typeof(CustomDispatcher),
            "Custom");

        var ex = Assert.Throws<ArgumentException>(() =>
            GeneratedServiceRegistry.Register<ICustomRegisteredService>(
                _ => new CustomProxy(),
                _ => new CustomDispatcher(),
                mismatched));

        Assert.Contains("registered for", ex.Message);
    }

    [Fact]
    public void Register_MetadataMissingServiceName_ThrowsArgumentException()
    {
        var noName = new GeneratedService(
            typeof(ICustomRegisteredService),
            typeof(CustomProxy),
            typeof(CustomDispatcher),
            string.Empty);

        var ex = Assert.Throws<ArgumentException>(() =>
            GeneratedServiceRegistry.Register<ICustomRegisteredService>(
                _ => new CustomProxy(),
                _ => new CustomDispatcher(),
                noName));

        Assert.Contains("service name", ex.Message);
    }

    [Fact]
    public void Register_WithValidMetadata_MakesServiceResolvable()
    {
        // A full round-trip: register a hand-authored service interface, then resolve its proxy,
        // dispatcher, and metadata through the public API.
        GeneratedServiceRegistry.Register<ICustomRegisteredService>(
            _ => new CustomProxy(),
            _ => new CustomDispatcher(),
            ValidCustomService());

        var metadata = GeneratedServiceRegistry.GetService<ICustomRegisteredService>();
        var proxy = GeneratedServiceRegistry.CreateProxy<ICustomRegisteredService>(new RecordingInvoker());
        var dispatcher = GeneratedServiceRegistry.CreateDispatcher<ICustomRegisteredService>(new CustomImplementation());

        Assert.Equal("Custom", metadata.ServiceName);
        Assert.IsType<CustomProxy>(proxy);
        Assert.Equal("Custom", dispatcher.ServiceName);
    }

    [Fact]
    public void Register_DuplicateRegistration_ReplacesPreviousFactory()
    {
        GeneratedServiceRegistry.Register<IReplaceableService>(
            _ => new ReplaceableProxyV1(),
            _ => new CustomDispatcher(),
            new GeneratedService(
                typeof(IReplaceableService),
                typeof(ReplaceableProxyV1),
                typeof(CustomDispatcher),
                "Replaceable"));

        GeneratedServiceRegistry.Register<IReplaceableService>(
            _ => new ReplaceableProxyV2(),
            _ => new CustomDispatcher(),
            new GeneratedService(
                typeof(IReplaceableService),
                typeof(ReplaceableProxyV2),
                typeof(CustomDispatcher),
                "Replaceable"));

        var proxy = GeneratedServiceRegistry.CreateProxy<IReplaceableService>(new RecordingInvoker());

        // The latest registration wins.
        Assert.IsType<ReplaceableProxyV2>(proxy);
    }

    private static GeneratedService ValidCustomService() =>
        new(
            typeof(ICustomRegisteredService),
            typeof(CustomProxy),
            typeof(CustomDispatcher),
            "Custom");

    // --- Test-local service surfaces (no generator runs for these) ---

    private interface IUngeneratedCoverageService
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

    private sealed class CustomImplementation : ICustomRegisteredService
    {
        public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CustomProxy : ICustomRegisteredService
    {
        public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ReplaceableProxyV1 : IReplaceableService
    {
        public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ReplaceableProxyV2 : IReplaceableService
    {
        public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CustomDispatcher : IServiceDispatcher
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

    private sealed class RecordingServiceSink : IDotBoxDServiceRegistrationSink
    {
        public List<Type> ServiceTypes { get; } = new();

        public void AddService<TService, TImplementation>()
            where TService : class
            where TImplementation : TService =>
            ServiceTypes.Add(typeof(TService));
    }

    private sealed class RecordingGeneratedSink : IDotBoxDGeneratedServiceRegistrationSink
    {
        public List<Type> ServiceTypes { get; } = new();

        public void AddService<TService, TProxy, TDispatcher>()
            where TService : class
            where TProxy : TService
            where TDispatcher : IServiceDispatcher =>
            ServiceTypes.Add(typeof(TService));
    }

    /// <summary>
    /// Minimal <see cref="IRpcInvoker"/> that lets a generated proxy run without a transport. Every
    /// no-request/response call returns a canned <see cref="ServerStatus"/> so proxy routing is
    /// observable, and records the last service name forwarded by the proxy.
    /// </summary>
    private sealed class RecordingInvoker : IRpcInvoker
    {
        public string? LastService { get; private set; }

        public Task<TResponse> InvokeAsync<TRequest, TResponse>(
            string service, string method, TRequest request, CancellationToken ct = default)
        {
            LastService = service;
            return Task.FromResult(CannedResponse<TResponse>());
        }

        public Task<TResponse> InvokeAsync<TResponse>(string service, string method, CancellationToken ct = default)
        {
            LastService = service;
            return Task.FromResult(CannedResponse<TResponse>());
        }

        public Task InvokeAsync<TRequest>(string service, string method, TRequest request, CancellationToken ct = default)
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
            string service, string instanceId, string method, TRequest request, CancellationToken ct = default)
        {
            LastService = service;
            return Task.FromResult(CannedResponse<TResponse>());
        }

        public Task<TResponse> InvokeOnInstanceAsync<TResponse>(
            string service, string instanceId, string method, CancellationToken ct = default)
        {
            LastService = service;
            return Task.FromResult(CannedResponse<TResponse>());
        }

        public Task InvokeOnInstanceAsync<TRequest>(
            string service, string instanceId, string method, TRequest request, CancellationToken ct = default)
        {
            LastService = service;
            return Task.CompletedTask;
        }

        public Task InvokeOnInstanceAsync(
            string service, string instanceId, string method, CancellationToken ct = default)
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

}
