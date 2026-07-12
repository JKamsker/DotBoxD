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
        ArgumentNullException.ThrowIfNull(adapter);
        var eventName = adapter.EventName;
        var parameters = adapter.Parameters;
        PluginEventAdapterShapeValidator.Validate(adapter, eventName, parameters);
        var shape = new PluginEventShape(eventName, parameters);
        validateRegistration?.Invoke();
        lock (_gate)
        {
            validateRegistration?.Invoke();
            ValidateEventNameShape(typeof(TEvent), shape);
            // Capture the type-erased wire closure here — the single store site both the explicit Register path and
            // the lazy Resolve auto-register path flow through — so the router can wire by event name with no
            // reflection, over the SAME adapter instance Resolve returns (preserving pipeline adapter identity).
            _adapters[typeof(TEvent)] = new(adapter, shape, new ErasedPluginEventAdapter<TEvent>(adapter));
            _adapterSnapshot = _adapters.ToArray();
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
        Register(discovered);
        return discovered;
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

    private struct RegisteredPluginEventResolution
    {
        private readonly string _eventName;
        private readonly bool _allowTypeNameMatch;
        private RegisteredPluginEventAdapter _exactMatch;
        private int _exactCount;
        private RegisteredPluginEventAdapter _typeNameMatch;
        private int _typeNameCount;
        private RegisteredPluginEventAdapter _suffixMatch;
        private int _suffixCount;

        public RegisteredPluginEventResolution(string eventName)
        {
            _eventName = eventName;
            _allowTypeNameMatch = EventNameMatch.HasTopLevelQualifier(eventName);
            _exactMatch = default;
            _exactCount = 0;
            _typeNameMatch = default;
            _typeNameCount = 0;
            _suffixMatch = default;
            _suffixCount = 0;
        }

        public void Add(Type eventType, RegisteredPluginEventAdapter registered)
        {
            if (string.Equals(registered.Shape.EventName, _eventName, StringComparison.Ordinal))
            {
                AddExactMatch(registered);
                return;
            }

            if (_allowTypeNameMatch &&
                eventType.FullName is { } fullName &&
                EventNameMatch.Matches(fullName, _eventName))
            {
                AddTypeNameMatch(registered);
            }

            if (EventNameMatch.Matches(registered.Shape.EventName, _eventName))
            {
                AddSuffixMatch(registered);
            }
        }

        private void AddExactMatch(RegisteredPluginEventAdapter registered)
        {
            if (_exactCount == 0)
            {
                _exactMatch = registered;
            }

            _exactCount++;
        }

        private void AddSuffixMatch(RegisteredPluginEventAdapter registered)
        {
            if (_suffixCount == 0)
            {
                _suffixMatch = registered;
            }

            _suffixCount++;
        }

        private void AddTypeNameMatch(RegisteredPluginEventAdapter registered)
        {
            if (_typeNameCount == 0)
            {
                _typeNameMatch = registered;
            }

            _typeNameCount++;
        }

        public readonly bool TryResolve(bool rejectAmbiguous, out RegisteredPluginEventAdapter resolved)
        {
            if (_exactCount == 1 || (_exactCount > 1 && !rejectAmbiguous))
            {
                resolved = _exactMatch;
                return true;
            }

            if (_exactCount == 0 && _typeNameCount == 1)
            {
                resolved = _typeNameMatch;
                return true;
            }

            // Suffix matches require uniqueness for BOTH callers: adapters that merely share a simple-name tail can
            // have different shapes (DBXK034 only compares exact names), so there is no well-defined shape to validate
            // against and no unambiguous adapter to wire — picking by registration order would be wrong either way.
            if (_exactCount == 0 && _typeNameCount == 0 && _suffixCount == 1)
            {
                resolved = _suffixMatch;
                return true;
            }

            // No match, or an ambiguous tier we refuse to resolve by registration order.
            resolved = default;
            return false;
        }
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
