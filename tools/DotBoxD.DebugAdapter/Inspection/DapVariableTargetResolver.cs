namespace DotBoxD.DebugAdapter;

internal static class DapVariableTargetResolver
{
    public static DapVariableTarget Resolve(
        DapVariableHandle handle,
        string childName,
        IReadOnlyList<DapSourceVariableBinding> bindings,
        DapVariableStore variables)
    {
        if (handle.VariableName is null)
        {
            return new DapVariableTarget(childName, null);
        }

        var authoredMember = handle.VariableName + "." + childName;
        if (bindings.Any(binding =>
                binding.DisplayValue is null &&
                string.Equals(binding.SourceName, authoredMember, StringComparison.Ordinal)))
        {
            return new DapVariableTarget(authoredMember, null);
        }

        return new DapVariableTarget(
            handle.VariableName,
            variables.ChildPath(handle, childName));
    }
}

internal readonly record struct DapVariableTarget(
    string Expression,
    IReadOnlyList<object>? Path);
