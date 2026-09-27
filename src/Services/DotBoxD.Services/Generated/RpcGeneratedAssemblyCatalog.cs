using System.Reflection;
using System.Runtime.CompilerServices;

namespace DotBoxD.Services.Generated;

internal static class RpcGeneratedAssemblyCatalog
{
    private const string GeneratedFactoryTypeName = "DotBoxD.Services.Generated.DotBoxDGenerated";

    // Discovery caches follow assembly lifetime. Explicit global service registrations retain their
    // own factories through GeneratedServiceRegistry, independently of these lookup results.
    private static readonly ConditionalWeakTable<Assembly, AssemblyCache> s_caches = new();

    public static bool EnsureRegistered(Assembly assembly)
    {
        var cache = GetCache(assembly);
        var registration = Volatile.Read(ref cache.Registration);
        if (registration is null)
        {
            var created = new Lazy<bool>(
                () => RegisterGeneratedFactory(assembly),
                LazyThreadSafetyMode.ExecutionAndPublication);
            registration = Interlocked.CompareExchange(ref cache.Registration, created, null) ?? created;
        }

        try
        {
            return registration.Value;
        }
        catch
        {
            EvictFaultedAttempt(assembly, registration);
            throw;
        }
    }

    /// <summary>
    /// Removes only the faulted attempt this caller actually holds. Compare-exchange preserves a
    /// successor another thread installed after our attempt faulted. Internal so a deterministic test
    /// can exercise the successor-preservation behaviour.
    /// </summary>
    internal static void EvictFaultedAttempt(Assembly assembly, Lazy<bool> faultedRegistration)
    {
        if (s_caches.TryGetValue(assembly, out var cache))
        {
            Interlocked.CompareExchange(ref cache.Registration, null, faultedRegistration);
        }
    }

    // --- Test accessors (for the deterministic fault-recovery successor-preservation test) ---
    internal static void SetRegistrationAttemptForTest(Assembly assembly, Lazy<bool> attempt) =>
        Volatile.Write(ref GetCache(assembly).Registration, attempt);

    internal static Lazy<bool>? GetRegistrationAttemptForTest(Assembly assembly) =>
        s_caches.TryGetValue(assembly, out var cache) ? Volatile.Read(ref cache.Registration) : null;

    private static bool RegisterGeneratedFactory(Assembly assembly)
    {
        var generatedType = FindGeneratedType(assembly);
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
            throw new InvalidOperationException(
                $"DotBoxD generated factory registration failed for assembly '{assembly.FullName}'.",
                ex);
        }
    }

    public static IReadOnlyList<GeneratedService> GetServices(Assembly assembly)
    {
        var cache = GetCache(assembly);
        if (Volatile.Read(ref cache.Services) is { } services)
        {
            return services;
        }

        // Loading can publish a catalog reentrantly, or race an explicit replacement. Preserve that
        // publication instead of overwriting it with the snapshot discovered by this call.
        var loaded = LoadGeneratedServices(assembly, cache);
        return Interlocked.CompareExchange(ref cache.Services, loaded, null) ?? loaded;
    }

    public static void PublishServices(Assembly assembly, IReadOnlyList<GeneratedService> services) =>
        Volatile.Write(ref GetCache(assembly).Services, GeneratedServiceCatalogSnapshot.Snapshot(services));

    public static void RegisterServices(Assembly assembly, IRpcServiceRegistrationSink sink)
    {
        var cache = GetCache(assembly);
        var registrar = Volatile.Read(ref cache.ServiceSink);
        if (registrar is null)
        {
            var created = CreateSinkRegistrar<IRpcServiceRegistrationSink>(assembly, "RegisterServices");
            registrar = Interlocked.CompareExchange(ref cache.ServiceSink, created, null) ?? created;
        }

        registrar.Invoke(sink);
    }

    public static void RegisterGeneratedServices(Assembly assembly, IRpcGeneratedServiceRegistrationSink sink)
    {
        var cache = GetCache(assembly);
        var registrar = Volatile.Read(ref cache.GeneratedSink);
        if (registrar is null)
        {
            var created = CreateSinkRegistrar<IRpcGeneratedServiceRegistrationSink>(assembly, "RegisterGeneratedServices");
            registrar = Interlocked.CompareExchange(ref cache.GeneratedSink, created, null) ?? created;
        }

        registrar.Invoke(sink);
    }

    private static IReadOnlyList<GeneratedService> LoadGeneratedServices(Assembly assembly, AssemblyCache cache)
    {
        var generatedType = FindGeneratedType(assembly);
        if (generatedType is null)
        {
            return Array.Empty<GeneratedService>();
        }

        EnsureRegistered(assembly);
        if (Volatile.Read(ref cache.Services) is { } services)
        {
            return services;
        }

        var property = generatedType.GetProperty("Services", BindingFlags.Public | BindingFlags.Static);
        if (property is not null &&
            ReadLegacyServicesProperty(assembly, generatedType, property) is IReadOnlyList<GeneratedService> legacyServices)
        {
            return GeneratedServiceCatalogSnapshot.Snapshot(legacyServices, validateImplementationTypes: false);
        }

        throw new InvalidOperationException(
            $"DotBoxD generated factory type '{GeneratedFactoryTypeName}' in assembly '{assembly.FullName}' " +
            "did not publish a compatible Services catalog.");
    }

    private static object? ReadLegacyServicesProperty(Assembly assembly, Type generatedType, PropertyInfo property)
    {
        try
        {
            return property.GetValue(null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw new InvalidOperationException(
                $"DotBoxD generated factory type '{generatedType.FullName}' in assembly '{assembly.FullName}' " +
                $"failed while reading legacy {property.Name} catalog.",
                ex.InnerException);
        }
    }

    private static SinkRegistrar<TSink> CreateSinkRegistrar<TSink>(Assembly assembly, string methodName)
        where TSink : class
    {
        var generatedType = FindGeneratedType(assembly);
        if (generatedType is null)
        {
            return new SinkRegistrar<TSink>(register: null);
        }

        EnsureRegistered(assembly);

        var method = generatedType.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(TSink) },
            null);
        if (method is null)
        {
            throw new InvalidOperationException(
                $"DotBoxD generated factory type '{GeneratedFactoryTypeName}' in assembly '{assembly.FullName}' " +
                $"did not publish a compatible {methodName} method.");
        }

        if (method.ReturnType != typeof(void))
        {
            throw IncompatibleSinkMethod(assembly, generatedType, methodName);
        }

        try
        {
            return new SinkRegistrar<TSink>(
                (Action<TSink>)Delegate.CreateDelegate(typeof(Action<TSink>), method));
        }
        catch (ArgumentException ex)
        {
            throw IncompatibleSinkMethod(assembly, generatedType, methodName, ex);
        }
    }

    private static Type? FindGeneratedType(Assembly assembly) =>
        assembly.GetType(GeneratedFactoryTypeName, throwOnError: false);

    private static InvalidOperationException IncompatibleSinkMethod(
        Assembly assembly,
        Type generatedType,
        string methodName,
        Exception? innerException = null) =>
        new(
            $"DotBoxD generated factory type '{generatedType.FullName}' in assembly '{assembly.FullName}' " +
            $"published an incompatible {methodName} method.",
            innerException);

    private static AssemblyCache GetCache(Assembly assembly) => s_caches.GetValue(assembly, static _ => new AssemblyCache());

    private sealed class AssemblyCache
    {
        public IReadOnlyList<GeneratedService>? Services;
        public Lazy<bool>? Registration;
        public SinkRegistrar<IRpcServiceRegistrationSink>? ServiceSink;
        public SinkRegistrar<IRpcGeneratedServiceRegistrationSink>? GeneratedSink;
    }

    private sealed class SinkRegistrar<TSink>
        where TSink : class
    {
        private readonly Action<TSink>? _register;

        public SinkRegistrar(Action<TSink>? register) => _register = register;

        public void Invoke(TSink sink) => _register?.Invoke(sink);
    }
}
