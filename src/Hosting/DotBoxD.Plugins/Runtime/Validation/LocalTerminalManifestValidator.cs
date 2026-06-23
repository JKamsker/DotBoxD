namespace DotBoxD.Plugins.Runtime;

internal static class LocalTerminalManifestValidator
{
    public static void ValidateRunLocal<TProjected>(PluginPackage package, bool expectedWholeEvent)
    {
        var subscription = package.Manifest.Subscriptions.Count > 0 ? package.Manifest.Subscriptions[0] : null;
        if (subscription is not { LocalTerminal: true })
        {
            throw new InvalidOperationException(
                $"Hook package '{package.Manifest.PluginId}' does not declare localTerminal metadata.");
        }

        var actualWholeEvent = subscription.ProjectedType is null;
        if (actualWholeEvent != expectedWholeEvent)
        {
            throw new InvalidOperationException(
                $"Hook package '{package.Manifest.PluginId}' projectedType does not match the RunLocal install path.");
        }

        if (!actualWholeEvent &&
            !ProjectedTypeMatches(subscription.ProjectedType!, typeof(TProjected)))
        {
            throw new InvalidOperationException(
                $"Hook package '{package.Manifest.PluginId}' projectedType '{subscription.ProjectedType}' does not match " +
                $"handler type '{typeof(TProjected).FullName ?? typeof(TProjected).Name}'.");
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
           type.GetInterfaces().Any(static candidate =>
               candidate.IsGenericType &&
               candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>));

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
