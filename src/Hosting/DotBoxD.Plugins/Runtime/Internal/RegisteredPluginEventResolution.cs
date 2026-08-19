namespace DotBoxD.Plugins.Runtime;

internal struct RegisteredPluginEventResolution
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
