using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;
using DotBoxD.Plugins.Runtime.Validation;

namespace DotBoxD.Plugins.Runtime;

internal static class LocalTerminalManifestValidator
{
    public static void ValidateRunLocal<TProjected>(PluginPackage package)
    {
        var subscription = package.Manifest.Subscriptions.Count > 0 ? package.Manifest.Subscriptions[0] : null;
        if (subscription is not { LocalTerminal: true })
        {
            throw new InvalidOperationException(
                $"Hook package '{package.Manifest.PluginId}' does not declare localTerminal metadata.");
        }

        if (subscription.ProjectedType is null)
        {
            throw new InvalidOperationException(
                $"Hook package '{package.Manifest.PluginId}' does not declare projectedType metadata.");
        }

        if (!ProjectedTypeMatches(subscription.ProjectedType, typeof(TProjected)))
        {
            throw new InvalidOperationException(
                $"Hook package '{package.Manifest.PluginId}' projectedType '{subscription.ProjectedType}' does not match " +
                $"handler type '{typeof(TProjected).FullName ?? typeof(TProjected).Name}'.");
        }

        ValidateHandleReturnType<TProjected>(package);
    }

    private static void ValidateHandleReturnType<TProjected>(PluginPackage package)
    {
        if (!PluginEntrypointIndex.Build(package).TryGet(package.Entrypoints.Handle, out var handle) ||
            handle.ReturnType == SandboxType.Unit)
        {
            return;
        }

        var expected = KernelRpcMarshaller.SandboxTypeOf(typeof(TProjected));
        if (handle.ReturnType != expected)
        {
            throw new InvalidOperationException(
                $"Hook package '{package.Manifest.PluginId}' projectedType '{package.Manifest.Subscriptions[0].ProjectedType}' " +
                $"declares handler type '{typeof(TProjected).FullName ?? typeof(TProjected).Name}', but Handle returns " +
                $"'{handle.ReturnType}' instead of '{expected}'.");
        }
    }

    private static bool ProjectedTypeMatches(string declared, Type expected)
        => declared switch
        {
            "bool" => expected == typeof(bool),
            "int" => expected == typeof(int) || IsEnum(expected),
            "long" => expected == typeof(long) || IsEnum(expected),
            "double" => expected == typeof(double) || expected == typeof(float),
            "string" => expected == typeof(string),
            "guid" => expected == typeof(Guid),
            "list" => expected != typeof(string) &&
                !IsMap(expected) &&
                typeof(System.Collections.IEnumerable).IsAssignableFrom(expected),
            "map" => IsMap(expected),
            "record" => !IsKnownProjectedScalar(expected) &&
                !typeof(System.Collections.IEnumerable).IsAssignableFrom(expected) &&
                !IsMap(expected),
            _ => TypeNameMatches(declared, expected)
        };

    private static bool IsKnownProjectedScalar(Type type)
        => type == typeof(bool) ||
           type == typeof(int) ||
           type == typeof(long) ||
           type == typeof(double) ||
           type == typeof(float) ||
           type == typeof(string) ||
           type == typeof(Guid) ||
           IsEnum(type);

    private static bool IsEnum(Type type)
        => type.IsEnum;

    private static bool IsMap(Type type)
        => typeof(System.Collections.IDictionary).IsAssignableFrom(type) ||
           IsGenericMap(type) ||
           type.GetInterfaces().Any(IsGenericMap);

    private static bool IsGenericMap(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        // A RunLocal map projection whose handler parameter is declared as IReadOnlyDictionary<,> (or a
        // Dictionary<,>, which implements both) is treated as a map by the generator/RPC mapper; accept it here
        // too so the decoder's materialized Dictionary is not rejected before the callback is even registered.
        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(IDictionary<,>) ||
               definition == typeof(IReadOnlyDictionary<,>);
    }

    private static bool TypeNameMatches(string declared, Type expected)
    {
        var expectedName = expected.FullName ?? expected.Name;
        return string.Equals(Normalize(declared), Normalize(expectedName), StringComparison.Ordinal);
    }

    private static string Normalize(string name)
    {
        const string globalPrefix = "global::";
        return (name.StartsWith(globalPrefix, StringComparison.Ordinal)
                ? name[globalPrefix.Length..]
                : name)
            .Replace('+', '.');
    }
}
