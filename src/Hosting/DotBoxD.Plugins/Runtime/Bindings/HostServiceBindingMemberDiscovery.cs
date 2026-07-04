using System.Reflection;

namespace DotBoxD.Hosting.Execution;

internal static class HostServiceBindingMemberDiscovery
{
    private const string ExtensibleControlType = "DotBoxD.Abstractions.IExtensibleControl";
    private const string ServiceControlType = "DotBoxD.Abstractions.IServiceControl";
    private const string DotBoxDServiceAttributeType = "DotBoxD.Services.Attributes.DotBoxDServiceAttribute";

    public static IEnumerable<MethodInfo> ServiceMethods(Type serviceType)
        => ServiceTypes(serviceType).SelectMany(static type => type.GetMethods());

    public static IEnumerable<PropertyInfo> ServiceProperties(Type serviceType)
        => ServiceTypes(serviceType).SelectMany(static type => type.GetProperties());

    public static bool ShouldSkipMethod(MethodInfo method)
        => method.IsSpecialName ||
           method.IsGenericMethodDefinition ||
           IsControlType(method.DeclaringType);

    public static bool ShouldSkipProperty(PropertyInfo property)
        => property.GetMethod is null ||
           property.GetMethod.IsStatic ||
           property.GetIndexParameters().Length != 0 ||
           IsControlType(property.DeclaringType);

    public static void RejectUnsupportedExplicitPropertyBinding(PropertyInfo property)
    {
        if (property.GetIndexParameters().Length == 0 ||
            property.GetCustomAttribute<HostBindingAttribute>() is not { } binding)
        {
            return;
        }

        throw new InvalidOperationException(
            $"HostBinding property '{property.DeclaringType?.FullName}.{property.Name}' is an unsupported indexer; " +
            $"binding '{binding.BindingId}' cannot be registered as a zero-argument host service property.");
    }

    public static void RejectUnsupportedGenericHostBindingMethod(MethodInfo method)
    {
        if (!method.IsGenericMethodDefinition ||
            method.GetCustomAttribute<HostBindingAttribute>() is not { } binding)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Host service method '{method.DeclaringType?.FullName}.{method.Name}' declares generic " +
            $"[HostBinding] route '{binding.BindingId}', but generic HostBinding methods are not supported.");
    }

    public static bool HasDotBoxDServiceAttribute(Type type)
        => HasDirectDotBoxDServiceAttribute(type) || type.GetInterfaces().Any(HasDirectDotBoxDServiceAttribute);

    private static IEnumerable<Type> ServiceTypes(Type serviceType)
    {
        foreach (var inherited in serviceType.GetInterfaces().OrderBy(static type => type.FullName, StringComparer.Ordinal))
        {
            yield return inherited;
        }

        yield return serviceType;
    }

    private static bool IsControlType(MemberInfo? type)
        => type is Type t &&
           (string.Equals(t.FullName, ExtensibleControlType, StringComparison.Ordinal) ||
            string.Equals(t.FullName, ServiceControlType, StringComparison.Ordinal));

    private static bool HasDirectDotBoxDServiceAttribute(Type type)
        => type.GetCustomAttributes(inherit: false)
            .Any(attribute => string.Equals(
                attribute.GetType().FullName,
                DotBoxDServiceAttributeType,
                StringComparison.Ordinal));
}
