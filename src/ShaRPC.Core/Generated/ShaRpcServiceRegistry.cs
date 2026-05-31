using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using ShaRPC.Core.Client;
using ShaRPC.Core.Server;

namespace ShaRPC.Core.Generated;

/// <summary>
/// Runtime registry populated by ShaRPC-generated service factories.
/// </summary>
public static class ShaRpcServiceRegistry
{
    private const string GeneratedFactoryTypeName = "ShaRPC.Generated.ShaRpcGenerated";

    private static readonly ConcurrentDictionary<Type, RegisteredService> s_services = new();
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyList<ShaRpcGeneratedService>> s_serviceCatalogs = new();
    private static readonly ConcurrentDictionary<Assembly, bool> s_registrationAttempts = new();

    /// <summary>
    /// Registers generated factories for a service interface.
    /// </summary>
    public static void Register<TService>(
        Func<IShaRpcClient, TService> proxyFactory,
        Func<object, IServiceDispatcher> dispatcherFactory)
        where TService : class =>
        Register(
            proxyFactory,
            dispatcherFactory,
            new ShaRpcGeneratedService(
                typeof(TService),
                typeof(TService),
                typeof(IServiceDispatcher),
                typeof(TService).Name));

    /// <summary>
    /// Registers generated factories and generated service metadata for a service interface.
    /// </summary>
    public static void Register<TService>(
        Func<IShaRpcClient, TService> proxyFactory,
        Func<object, IServiceDispatcher> dispatcherFactory,
        ShaRpcGeneratedService service)
        where TService : class
    {
        if (proxyFactory is null)
        {
            throw new ArgumentNullException(nameof(proxyFactory));
        }

        if (dispatcherFactory is null)
        {
            throw new ArgumentNullException(nameof(dispatcherFactory));
        }

        ValidateService<TService>(service);

        s_services[typeof(TService)] = new RegisteredService(
            client => proxyFactory(client)!,
            dispatcherFactory,
            service);
    }

    /// <summary>
    /// Gets generated metadata for <typeparamref name="TService"/>.
    /// </summary>
    public static ShaRpcGeneratedService GetService<TService>()
        where TService : class =>
        GetService(typeof(TService));

    /// <summary>
    /// Gets generated metadata for <paramref name="serviceInterface"/>.
    /// </summary>
    public static ShaRpcGeneratedService GetService(Type serviceInterface) =>
        Resolve(serviceInterface).Service;

    /// <summary>
    /// Gets generated service metadata from <paramref name="assembly"/> without scanning its types.
    /// </summary>
    public static IReadOnlyList<ShaRpcGeneratedService> GetServices(Assembly assembly)
    {
        if (assembly is null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        return s_serviceCatalogs.GetOrAdd(assembly, static assembly => LoadGeneratedServices(assembly));
    }

    /// <summary>
    /// Creates the generated client proxy for <typeparamref name="TService"/>.
    /// </summary>
    public static TService CreateProxy<TService>(IShaRpcClient client)
        where TService : class =>
        (TService)CreateProxy(typeof(TService), client);

    /// <summary>
    /// Creates the generated client proxy for <paramref name="serviceInterface"/>.
    /// </summary>
    public static object CreateProxy(Type serviceInterface, IShaRpcClient client)
    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }
        var registration = Resolve(serviceInterface);
        return registration.CreateProxy(client);
    }

    /// <summary>
    /// Creates the generated server dispatcher for <paramref name="implementation"/>.
    /// </summary>
    public static IServiceDispatcher CreateDispatcher<TService>(TService implementation)
        where TService : class =>
        CreateDispatcher(typeof(TService), implementation);

    /// <summary>
    /// Creates the generated server dispatcher for <paramref name="implementation"/>.
    /// </summary>
    public static IServiceDispatcher CreateDispatcher(Type serviceInterface, object implementation)
    {
        if (implementation is null)
        {
            throw new ArgumentNullException(nameof(implementation));
        }
        var registration = Resolve(serviceInterface);
        if (!serviceInterface.IsInstanceOfType(implementation))
        {
            throw new ArgumentException(
                $"{implementation.GetType()} does not implement {FormatType(serviceInterface)}.",
                nameof(implementation));
        }

        return registration.CreateDispatcher(implementation);
    }

    private static RegisteredService Resolve(Type serviceInterface)
    {
        if (serviceInterface is null)
        {
            throw new ArgumentNullException(nameof(serviceInterface));
        }
        if (!serviceInterface.IsInterface)
        {
            throw new ArgumentException(
                $"Service type must be an interface. Received {FormatType(serviceInterface)}.",
                nameof(serviceInterface));
        }

        if (s_services.TryGetValue(serviceInterface, out var registration))
        {
            return registration;
        }

        var generatedTypeFound = EnsureGeneratedRegistration(serviceInterface);
        if (s_services.TryGetValue(serviceInterface, out registration))
        {
            return registration;
        }

        var assemblyName = serviceInterface.Assembly.GetName().Name ?? serviceInterface.Assembly.FullName;
        var reason = generatedTypeFound
            ? "the generated registry in that assembly did not register this service"
            : "no ShaRPC generated registry type was found in that assembly";
        throw new InvalidOperationException(
            $"No ShaRPC generated factory is registered for {FormatType(serviceInterface)}: {reason}. " +
            "Mark the interface with [ShaRpcService] and ensure the assembly that declares it runs the ShaRPC source generator. " +
            $"Assembly: {assemblyName}.");
    }

    private static bool EnsureGeneratedRegistration(Type serviceInterface)
    {
        var assembly = serviceInterface.Assembly;
        if (!s_registrationAttempts.TryAdd(assembly, true))
        {
            return assembly.GetType(GeneratedFactoryTypeName, throwOnError: false) is not null;
        }

        var generatedType = assembly.GetType(GeneratedFactoryTypeName, throwOnError: false);
        if (generatedType is null)
        {
            return false;
        }

        try
        {
            RuntimeHelpers.RunClassConstructor(generatedType.TypeHandle);
            return true;
        }
        catch (Exception ex)
        {
            s_registrationAttempts.TryRemove(assembly, out _);
            throw new InvalidOperationException(
                $"ShaRPC generated factory registration failed for assembly '{assembly.FullName}'.",
                ex);
        }
    }

    private static IReadOnlyList<ShaRpcGeneratedService> LoadGeneratedServices(Assembly assembly)
    {
        var generatedType = assembly.GetType(GeneratedFactoryTypeName, throwOnError: false);
        if (generatedType is null)
        {
            s_registrationAttempts.TryAdd(assembly, true);
            return Array.Empty<ShaRpcGeneratedService>();
        }

        try
        {
            s_registrationAttempts.TryAdd(assembly, true);
            RuntimeHelpers.RunClassConstructor(generatedType.TypeHandle);
        }
        catch (Exception ex)
        {
            s_registrationAttempts.TryRemove(assembly, out _);
            throw new InvalidOperationException(
                $"ShaRPC generated factory registration failed for assembly '{assembly.FullName}'.",
                ex);
        }

        var property = generatedType.GetProperty("Services", BindingFlags.Public | BindingFlags.Static);
        if (property?.GetValue(null) is IReadOnlyList<ShaRpcGeneratedService> services)
        {
            return services;
        }

        throw new InvalidOperationException(
            $"ShaRPC generated factory type '{GeneratedFactoryTypeName}' in assembly '{assembly.FullName}' " +
            "does not expose a compatible Services catalog.");
    }

    private static void ValidateService<TService>(ShaRpcGeneratedService service)
        where TService : class
    {
        if (service.ServiceType is null)
        {
            throw new ArgumentException("Generated service metadata must include a service type.", nameof(service));
        }
        if (service.ProxyType is null)
        {
            throw new ArgumentException("Generated service metadata must include a proxy type.", nameof(service));
        }
        if (service.DispatcherType is null)
        {
            throw new ArgumentException("Generated service metadata must include a dispatcher type.", nameof(service));
        }
        if (string.IsNullOrEmpty(service.ServiceName))
        {
            throw new ArgumentException("Generated service metadata must include a service name.", nameof(service));
        }
        if (service.ServiceType != typeof(TService))
        {
            throw new ArgumentException(
                $"Generated service metadata describes {FormatType(service.ServiceType)}, " +
                $"but it was registered for {FormatType(typeof(TService))}.",
                nameof(service));
        }
    }

    private static string FormatType(Type type) => type.FullName ?? type.Name;

    private sealed class RegisteredService
    {
        public RegisteredService(
            Func<IShaRpcClient, object> proxyFactory,
            Func<object, IServiceDispatcher> dispatcherFactory,
            ShaRpcGeneratedService service)
        {
            CreateProxy = proxyFactory;
            CreateDispatcher = dispatcherFactory;
            Service = service;
        }

        public Func<IShaRpcClient, object> CreateProxy { get; }

        public Func<object, IServiceDispatcher> CreateDispatcher { get; }

        public ShaRpcGeneratedService Service { get; }
    }
}
