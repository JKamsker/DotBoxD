using System.Reflection;

namespace DotBoxD.Plugins.Runtime;

internal static class LiveContextPropertyDiscovery
{
    public static IEnumerable<PropertyInfo> GetProperties(Type type)
    {
        foreach (var property in type.GetProperties())
        {
            yield return property;
        }

        // Interface reflection omits inherited properties; GetInterfaces includes each
        // ancestor once, including shared ancestors in a diamond.
        foreach (var inherited in type.GetInterfaces())
        {
            foreach (var property in inherited.GetProperties())
            {
                yield return property;
            }
        }
    }
}
