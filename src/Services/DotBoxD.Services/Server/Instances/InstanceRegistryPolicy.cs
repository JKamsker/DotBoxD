namespace DotBoxD.Services.Server;

internal static class InstanceRegistryPolicy
{
    internal static void ValidateKeys(string serviceName, string instanceId)
    {
        ThrowIfInvalidKey(serviceName, nameof(serviceName), "Service name");
        ThrowIfInvalidKey(instanceId, nameof(instanceId), "Instance id");
    }

    internal static void ThrowIfInvalidKey(string value, string paramName, string label)
    {
        if (IsInvalidKey(value))
        {
            throw new ArgumentException(
                label + " must not be null, empty, or whitespace.",
                paramName);
        }
    }

    internal static bool IsInvalidKey(string? value) => string.IsNullOrWhiteSpace(value);

    internal static bool ContainsReference(IEnumerable<object> instances, object candidate)
    {
        foreach (var instance in instances)
        {
            if (ReferenceEquals(instance, candidate))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool ContainsPendingDisposal(
        IEnumerable<InstanceRegistryDisposal> disposals,
        object instance)
    {
        foreach (var disposal in disposals)
        {
            if (ReferenceEquals(disposal.Instance, instance))
            {
                return true;
            }
        }

        return false;
    }

    internal static void RemoveReference(List<object> instances, object candidate)
    {
        for (var index = 0; index < instances.Count; index++)
        {
            if (ReferenceEquals(instances[index], candidate))
            {
                instances.RemoveAt(index);
                return;
            }
        }
    }
}
