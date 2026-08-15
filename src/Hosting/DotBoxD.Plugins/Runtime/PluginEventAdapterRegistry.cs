using System.Reflection;
using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Runtime.Input;

namespace DotBoxD.Plugins.Runtime;

public sealed class PluginEventAdapterRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, RegisteredPluginEventAdapter> _adapters = [];
    private volatile KeyValuePair<Type, RegisteredPluginEventAdapter>[] _adapterSnapshot = [];

    public void Register<TEvent>(IPluginEventAdapter<TEvent> adapter)
        => RegisterCore(adapter, validateRegistration: null);

    internal void Register<TEvent>(IPluginEventAdapter<TEvent> adapter, Action validateRegistration)
    {
        ArgumentNullException.ThrowIfNull(validateRegistration);
        RegisterCore(adapter, validateRegistration);
    }

    private void RegisterCore<TEvent>(IPluginEventAdapter<TEvent> adapter, Action? validateRegistration)
    {
        var registration = CreateRegistration(adapter);
        validateRegistration?.Invoke();
        lock (_gate)
        {
            validateRegistration?.Invoke();
            StoreRegistration<TEvent>(registration);
        }
    }

    public IPluginEventAdapter<TEvent> Resolve<TEvent>()
    {
        lock (_gate)
        {
            if (_adapters.TryGetValue(typeof(TEvent), out var registered))
            {
                return (IPluginEventAdapter<TEvent>)registered.Adapter;
            }
        }

        var discovered = TryDiscoverAdapter<TEvent>() ?? ConventionEventAdapter<TEvent>.Create();
        var registration = CreateRegistration(discovered);
        lock (_gate)
        {
            if (_adapters.TryGetValue(typeof(TEvent), out var registered))
            {
                return (IPluginEventAdapter<TEvent>)registered.Adapter;
            }

            StoreRegistration<TEvent>(registration);
            return discovered;
        }
    }

    private static RegisteredPluginEventAdapter CreateRegistration<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var eventName = adapter.EventName;
        var parameters = adapter.Parameters;
        PluginEventAdapterShapeValidator.Validate(adapter, eventName, parameters);
        var shape = new PluginEventShape(eventName, parameters);
        return new(adapter, shape, new ErasedPluginEventAdapter<TEvent>(adapter));
    }

    private void StoreRegistration<TEvent>(RegisteredPluginEventAdapter registration)
    {
        ValidateEventNameShape(typeof(TEvent), registration.Shape);
        _adapters[typeof(TEvent)] = registration;
        _adapterSnapshot = _adapters.ToArray();
    }

    internal bool TryResolveShape(string eventName, out PluginEventShape shape)
    {
        // Validation tolerates an ambiguous same-name collision. DBXK034 already forbids two adapters sharing an
        // EventName with DIFFERENT parameter shapes, so every same-name match yields the same shape — returning it
        // keeps install-time DBXK033 parameter validation running instead of silently skipping it (which would let
        // a malformed kernel install and only fail later at wiring/invocation).
        if (TryResolveRegistered(_adapterSnapshot, eventName, rejectAmbiguous: false, out var registered))
        {
            shape = registered.Shape;
            return true;
        }

        shape = default!;
        return false;
    }

    /// <summary>
    /// Resolves the type-erased, wire-capable adapter for <paramref name="eventName"/> (a manifest event name,
    /// possibly fully qualified) so the host-side router can wire an installed kernel to the right typed
    /// pipeline terminal with no reflection. Shares precedence with <see cref="TryResolveShape"/>, so an
    /// unambiguous resolution picks the same adapter a package was validated against; unlike validation, wiring
    /// <b>rejects</b> an ambiguous collision (returns <c>false</c>) rather than guess which event to wire to.
    /// Public as a composability seam — build custom by-name wiring on top of it when
    /// <see cref="PluginServer.WireHook"/>/<see cref="PluginServer.WireSubscription"/> don't fit; the adapter must
    /// be registered first (the router does not auto-register by name).
    /// </summary>
    public bool TryResolveErased(string eventName, out IErasedPluginEventAdapter adapter)
    {
        if (TryResolveRegistered(_adapterSnapshot, eventName, rejectAmbiguous: true, out var registered))
        {
            adapter = registered.Erased;
            return true;
        }

        adapter = null!;
        return false;
    }

    /// <summary>
    /// Single by-name resolution shared by wiring (<see cref="TryResolveErased"/>) and shape validation
    /// (<see cref="TryResolveShape"/>) so an unambiguous resolution picks the same adapter for both.
    /// Precedence:
    ///   1. Exact (ordinal) match on the adapter's reported name.
    ///   2. A fully-qualified match on the event TYPE's name (the dictionary key). Convention/hand-written
    ///      adapters report only the simple name, so two same-simple-name events in different namespaces are
    ///      indistinguishable by (1) and (3); the manifest records the FQN and the type's FullName is unique.
    ///   3. A qualified-vs-simple suffix bridge.
    /// Ambiguity handling differs only at the EXACT tier: when several adapters report the same exact name,
    /// <paramref name="rejectAmbiguous"/> wiring refuses it (so a kernel is never wired to the wrong event's
    /// pipeline), while validation accepts the first match — DBXK034 forbids same-exact-name adapters from having
    /// different shapes, so the validated shape is well-defined. The FQN and suffix tiers require a UNIQUE match
    /// for both callers: DBXK034 does not constrain adapters whose exact names merely share a simple-name tail, so
    /// their shapes may differ and there is no well-defined shape to validate against.
    /// </summary>
    private static bool TryResolveRegistered(
        KeyValuePair<Type, RegisteredPluginEventAdapter>[] adapters,
        string eventName,
        bool rejectAmbiguous,
        out RegisteredPluginEventAdapter resolved)
    {
        var resolution = new RegisteredPluginEventResolution(eventName);

        foreach (var entry in adapters)
        {
            resolution.Add(entry.Key, entry.Value);
        }

        return resolution.TryResolve(rejectAmbiguous, out resolved);
    }

    private static IPluginEventAdapter<TEvent>? TryDiscoverAdapter<TEvent>()
    {
        var adapterType = typeof(IPluginEventAdapter<TEvent>);
        foreach (var type in typeof(TEvent).Assembly.GetTypes())
        {
            if (type.IsAbstract || !adapterType.IsAssignableFrom(type))
            {
                continue;
            }

            object? instance;
            try
            {
                instance = StaticInstance(type) ?? Activator.CreateInstance(type);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw AdapterDiscoveryFailure(type, ex.InnerException);
            }

            return (IPluginEventAdapter<TEvent>)instance!;
        }

        return null;
    }

    private void ValidateEventNameShape(Type eventType, PluginEventShape shape)
    {
        foreach (var registered in _adapters)
        {
            if (registered.Key == eventType)
            {
                continue;
            }

            var current = registered.Value.Shape;
            if (!string.Equals(current.EventName, shape.EventName, StringComparison.Ordinal) ||
                PluginParameterShape.Matches(current.Parameters, shape.Parameters))
            {
                continue;
            }
            throw new SandboxValidationException([
                new SandboxDiagnostic("DBXK034", $"Event adapter name '{shape.EventName}' is already registered with a different parameter shape.")
            ]);
        }
    }

    private static object? StaticInstance(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(p => string.Equals(p.Name, "Instance", StringComparison.Ordinal) &&
                                 type.IsAssignableFrom(p.PropertyType))
            ?.GetValue(null);

    private static InvalidOperationException AdapterDiscoveryFailure(Type type, Exception innerException)
        => new(
            "Plugin event adapter '" + type.FullName + "' failed during convention discovery.",
            innerException);
}

internal readonly record struct RegisteredPluginEventAdapter(
    object Adapter,
    PluginEventShape Shape,
    IErasedPluginEventAdapter Erased);
