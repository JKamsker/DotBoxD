using System.Collections.Immutable;

namespace DotBoxD.UI.Runtime;

internal sealed class UiInteractionState(UiPackage package)
{
    private readonly Dictionary<int, int> _parents = package.Nodes.SelectMany(n =>
        n.Children.Select(child => (Child: child, Parent: n.Id))).ToDictionary(p => p.Child, p => p.Parent);
    private HashSet<int> _blocked = [];

    public void Commit(ImmutableArray<UiPropertyValue> values)
        => _blocked = values.Where(p => p.PropertyId is UiPropertyId.Enabled or UiPropertyId.Visible && !p.Value.Boolean)
            .Select(p => p.NodeId).ToHashSet();

    public void Validate(int nodeId)
    {
        do
        {
            if (_blocked.Contains(nodeId))
            { throw new UiValidationException("Disabled or hidden UI nodes cannot receive input."); }
        } while (_parents.TryGetValue(nodeId, out nodeId));
    }

    public void Clear() { _parents.Clear(); _blocked.Clear(); }
}
