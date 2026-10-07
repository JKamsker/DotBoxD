using System.Collections.Immutable;

namespace DotBoxD.UI.Runtime;

internal sealed class UiBindings(UiPackage package, UiPolicy policy, UiKernelRunner kernels)
{
    private ImmutableArray<UiNode> _nodes = package.Nodes;
    private ImmutableArray<UiPropertyValue> _current = [];

    public async ValueTask<ImmutableArray<UiPropertyValue>> EvaluateAsync(
        IReadOnlyDictionary<int, UiValue> state,
        CancellationToken cancellationToken)
    {
        var values = ImmutableArray.CreateBuilder<UiPropertyValue>();
        var derived = new Dictionary<int, UiValue>();
        foreach (var node in _nodes)
        {
            foreach (var property in node.Properties)
            {
                UiValue value;
                if (property.Literal is { } literal)
                {
                    value = literal;
                }
                else if (property.StateSlotId != 0)
                {
                    value = state[property.StateSlotId];
                }
                else if (!derived.TryGetValue(property.BindingKernelId, out value!))
                {
                    value = await kernels.ExecuteAsync(property.BindingKernelId, state, cancellationToken).ConfigureAwait(false);
                    derived.Add(property.BindingKernelId, value);
                }

                UiValueValidator.Validate(value, policy);
                values.Add(new UiPropertyValue(node.Id, property.Id, value));
            }
        }

        return values.ToImmutable();
    }

    public ImmutableArray<UiPropertyValue> Changes(ImmutableArray<UiPropertyValue> next)
    {
        var changes = ImmutableArray.CreateBuilder<UiPropertyValue>();
        for (var i = 0; i < next.Length; i++)
        {
            if (i >= _current.Length || _current[i] != next[i])
            {
                changes.Add(next[i]);
            }
        }

        return changes.ToImmutable();
    }

    public void Commit(ImmutableArray<UiPropertyValue> next) => _current = next;
    public void Clear()
    {
        _nodes = [];
        _current = [];
    }
}
