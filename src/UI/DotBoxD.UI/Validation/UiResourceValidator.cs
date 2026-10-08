namespace DotBoxD.UI;

internal static class UiResourceValidator
{
    public static void Validate(UiPackage package, UiPolicy policy)
    {
        if (package.Resources.IsDefault || package.Resources.Length > policy.MaxResources)
        { throw new UiValidationException("UI resource collection exceeds the host limit."); }
        var ids = new HashSet<int>();
        var handles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in package.Resources)
        {
            if (resource is null || resource.Id <= 0 || !ids.Add(resource.Id) ||
                !ValidHandle(resource.Handle) || !handles.Add(resource.Handle))
            { throw new UiValidationException("Resource IDs and opaque handles must be valid and unique; URLs and paths are forbidden."); }
        }
        foreach (var node in package.Nodes)
        { ValidateImage(node, ids); }
    }

    private static void ValidateImage(UiNode node, HashSet<int> ids)
    {
        if (node is null || node.Properties.IsDefault || node.Primitive != UiPrimitive.Image)
        { return; }
        var source = node.Properties.FirstOrDefault(p => p?.Id == UiPropertyId.Resource);
        if (source?.Literal is not { Kind: UiValueKind.Int32 } value || source.StateSlotId != 0 ||
            source.BindingKernelId != 0 || !ids.Contains(value.Integer))
        { throw new UiValidationException("Images require a literal declared resource ID; dynamic URLs and handles are forbidden."); }
    }

    public static bool ValidHandle(string? handle) => !string.IsNullOrEmpty(handle) && handle.Length <= 128 &&
        handle.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}
