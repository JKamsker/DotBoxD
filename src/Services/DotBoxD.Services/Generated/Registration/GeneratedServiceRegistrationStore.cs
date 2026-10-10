using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using DotBoxD.Services.Server;

namespace DotBoxD.Services.Generated;

internal static class GeneratedServiceRegistrationStore
{
    // Ephemeron values may reference their key, factories and metadata without rooting the type.
    // This also handles constructed service types whose generic arguments are collectible.
    private static readonly ConditionalWeakTable<Type, Slot> Services = new();
    private static readonly object Gate = new();
    private static long s_version;

    public static long CurrentVersion => Volatile.Read(ref s_version);

    public static void Register(
        Type type,
        Func<IRpcInvoker, object> proxyFactory,
        Func<object, IServiceDispatcher> dispatcherFactory,
        GeneratedService service)
    {
        lock (Gate)
        {
            var version = s_version + 1;
            var registration = new RegisteredService(proxyFactory, dispatcherFactory,
                GeneratedServiceCatalogSnapshot.Snapshot(service), version);
            var slot = Services.GetValue(type, static _ => new Slot());
            Volatile.Write(ref slot.Registration, registration);
            Volatile.Write(ref s_version, version);
        }
    }

    public static bool TryGetValue(Type type, [NotNullWhen(true)] out RegisteredService? registration)
    {
        registration = Services.TryGetValue(type, out var slot) ? Volatile.Read(ref slot.Registration) : null;
        return registration is not null;
    }

    public static int Unregister(Type type)
    {
        lock (Gate)
        {
            if (!Services.Remove(type))
                return 0;

            Volatile.Write(ref s_version, s_version + 1);
            return 1;
        }
    }

    public static int UnregisterAssembly(Assembly assembly)
    {
        lock (Gate)
        {
            var removed = 0;
            foreach (var entry in Services)
            {
                if (entry.Key.Assembly == assembly && Services.Remove(entry.Key))
                    removed++;
            }

            if (removed != 0)
                Volatile.Write(ref s_version, s_version + 1);

            return removed;
        }
    }

    private sealed class Slot
    {
        public RegisteredService? Registration;
    }
}
