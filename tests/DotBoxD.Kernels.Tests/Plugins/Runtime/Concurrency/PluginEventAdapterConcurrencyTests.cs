using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Runtime;

public sealed class PluginEventAdapterConcurrencyTests
{
    private static readonly MethodInfo RegisterMethod =
        typeof(PluginEventAdapterRegistry).GetMethod(nameof(PluginEventAdapterRegistry.Register))!;

    [Fact]
    public async Task TryResolveErased_does_not_leak_collection_modified_when_registration_runs_concurrently()
    {
        var registry = new PluginEventAdapterRegistry();
        var eventTypes = CreateEventTypes(8_000);

        for (var i = 0; i < 1_500; i++)
        {
            Register(registry, eventTypes[i], i);
        }

        var failures = new ConcurrentQueue<Exception>();
        var registrationComplete = 0;
        using var start = new ManualResetEventSlim();

        var resolvers = Enumerable.Range(0, 4)
            .Select(_unused => Task.Run(() =>
            {
                start.Wait();
                while (Volatile.Read(ref registrationComplete) == 0 && failures.IsEmpty)
                {
                    try
                    {
                        registry.TryResolveErased("missing.concurrent.event", out _);
                    }
                    catch (InvalidOperationException ex) when (
                        ex.Message.Contains("Collection was modified", StringComparison.Ordinal))
                    {
                        failures.Enqueue(ex);
                    }
                }
            }))
            .ToArray();

        var registrar = Task.Run(() =>
        {
            start.Wait();
            try
            {
                for (var i = 1_500; i < eventTypes.Length && failures.IsEmpty; i++)
                {
                    Register(registry, eventTypes[i], i);
                }
            }
            finally
            {
                Volatile.Write(ref registrationComplete, 1);
            }
        });

        start.Set();
        await Task.WhenAll(resolvers.Append(registrar));

        if (failures.TryDequeue(out var failure))
        {
            Assert.Fail("Concurrent event adapter registration and name resolution leaked a raw collection-modified exception: " + failure.Message);
        }
    }

    private static Type[] CreateEventTypes(int count)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("PluginEventAdapterConcurrency" + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Events");
        var types = new Type[count];

        for (var i = 0; i < types.Length; i++)
        {
            types[i] = module.DefineType("GeneratedEvent" + i, TypeAttributes.Public | TypeAttributes.Sealed)
                .CreateTypeInfo()!
                .AsType();
        }

        return types;
    }

    private static void Register(PluginEventAdapterRegistry registry, Type eventType, int index)
    {
        var adapterType = typeof(TestEventAdapter<>).MakeGenericType(eventType);
        var adapter = Activator.CreateInstance(adapterType, "GeneratedEvent" + index)!;
        RegisterMethod.MakeGenericMethod(eventType).Invoke(registry, [adapter]);
    }

    private sealed class TestEventAdapter<TEvent>(string eventName) : IPluginEventAdapter<TEvent>
    {
        public string EventName { get; } = eventName;

        public IReadOnlyList<Parameter> Parameters { get; } = [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(TEvent e) => [];
    }
}
