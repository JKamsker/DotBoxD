namespace DotBoxD.UI;

internal static class UiTreeValidator
{
    public static void Validate(int root, Dictionary<int, UiNode> nodes,
        Dictionary<int, UiStateSlot> state, Dictionary<int, UiKernel> kernels, UiPolicy policy)
    {
        var pending = new Stack<(int Id, int Depth)>();
        var visited = new HashSet<int>();
        pending.Push((root, 1));
        while (pending.TryPop(out var next))
        {
            if (next.Depth > policy.MaxDepth || !nodes.TryGetValue(next.Id, out var node) || !visited.Add(next.Id))
            {
                throw new UiValidationException("UI tree contains unknown/shared/cyclic nodes or exceeds its depth limit.");
            }

            ValidateNode(node, state, kernels, policy);
            foreach (var child in node.Children)
            {
                pending.Push((child, next.Depth + 1));
            }
        }

        if (visited.Count != nodes.Count)
        {
            throw new UiValidationException("UI package contains unreachable nodes.");
        }
    }

    private static void ValidateNode(UiNode node, Dictionary<int, UiStateSlot> state,
        Dictionary<int, UiKernel> kernels, UiPolicy policy)
    {
        RequireNodeShape(node, policy);
        if (node.Children.Length > MaximumChildren(node.Primitive, policy))
        {
            throw new UiValidationException("UI primitive does not support these children.");
        }

        var properties = new HashSet<UiPropertyId>();
        foreach (var property in node.Properties)
        {
            UiPropertyValidator.Validate(property, node.Primitive, properties, state, kernels, policy);
        }
    }

    private static void RequireNodeShape(UiNode node, UiPolicy policy)
    {
        if (!policy.AllowedPrimitives.Contains(node.Primitive) || !Enum.IsDefined(node.Primitive) ||
            node.Children.IsDefault || node.Children.Length > policy.MaxChildren ||
            node.Properties.IsDefault || node.Properties.Length > 6)
        {
            throw new UiValidationException("Unsupported primitive or node structural limit exceeded.");
        }
    }

    private static int MaximumChildren(UiPrimitive primitive, UiPolicy policy) => primitive switch
    {
        UiPrimitive.Stack or UiPrimitive.Grid or UiPrimitive.Items => policy.MaxChildren,
        UiPrimitive.Border or UiPrimitive.ScrollViewer => 1,
        _ => 0
    };
}
