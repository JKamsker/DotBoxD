using System.Reflection;
using DotBoxD.Abstractions;
using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Hosting.Execution;

public static class HostServiceBindingExtensions
{
    private const string ExtensibleControlType = "DotBoxD.Abstractions.IExtensibleControl";
    private const string ServiceControlType = "DotBoxD.Abstractions.IServiceControl";
    private const string DotBoxDServiceAttributeType = "DotBoxD.Services.Attributes.DotBoxDServiceAttribute";

    public static SandboxHostBuilder AddBindingsFrom<TService>(
        this SandboxHostBuilder builder,
        TService implementation)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(implementation);

        var visited = new HashSet<Type>();
        var registeredBindings = new HashSet<string>(StringComparer.Ordinal);
        AddServiceBindings(builder, typeof(TService), implementation, visited, registeredBindings);
        return builder;
    }

    private static void AddServiceBindings(
        SandboxHostBuilder builder,
        Type serviceType,
        object implementation,
        HashSet<Type> visited,
        HashSet<string> registeredBindings)
    {
        if (!visited.Add(serviceType))
        {
            return;
        }

        foreach (var method in serviceType.GetMethods())
        {
            if (ShouldSkipMethod(method))
            {
                continue;
            }

            if (TryAddHandleServiceBindings(builder, serviceType, implementation, method, registeredBindings))
            {
                continue;
            }

            var target = ResolveTargetMethod(serviceType, implementation.GetType(), method);
            var capability = target.GetCustomAttribute<HostCapabilityAttribute>();
            if (capability is null)
            {
                throw new InvalidOperationException(
                    $"Host service method '{serviceType.FullName}.{method.Name}' must declare [HostCapability] on its implementation.");
            }

            AddBinding(
                builder,
                registeredBindings,
                HostServiceBindingFactory.CreateBinding(method, target, implementation, capability.Capability));
        }

        foreach (var property in serviceType.GetProperties())
        {
            if (ShouldSkipProperty(property))
            {
                continue;
            }

            var child = ReadPropertyValue(serviceType, implementation, property);
            if (child is null)
            {
                throw new InvalidOperationException(
                    $"Host service property '{serviceType.FullName}.{property.Name}' returned null.");
            }

            AddServiceBindings(builder, property.PropertyType, child, visited, registeredBindings);
        }
    }

    private static bool TryAddHandleServiceBindings(
        SandboxHostBuilder builder,
        Type parentServiceType,
        object parentImplementation,
        MethodInfo factoryMethod,
        HashSet<string> registeredBindings)
    {
        if (HostServiceBindingFactory.UnwrapReturnType(factoryMethod.ReturnType) is not { } handleServiceType ||
            !HasDotBoxDServiceAttribute(handleServiceType))
        {
            return false;
        }

        var targetFactory = ResolveTargetMethod(parentServiceType, parentImplementation.GetType(), factoryMethod);
        foreach (var handleMethod in handleServiceType.GetMethods())
        {
            if (ShouldSkipMethod(handleMethod))
            {
                continue;
            }

            var capability = handleMethod.GetCustomAttribute<HostCapabilityAttribute>();
            if (capability is null)
            {
                throw new InvalidOperationException(
                    $"Host service handle method '{handleServiceType.FullName}.{handleMethod.Name}' must declare [HostCapability].");
            }

            AddBinding(
                builder,
                registeredBindings,
                HostServiceBindingFactory.CreateHandleBinding(
                    factoryMethod,
                    targetFactory,
                    parentImplementation,
                    handleMethod,
                    capability.Capability));
        }

        return true;
    }

    private static void AddBinding(
        SandboxHostBuilder builder,
        HashSet<string> registeredBindings,
        BindingDescriptor descriptor)
    {
        if (registeredBindings.Add(descriptor.Id))
        {
            builder.AddBinding(descriptor);
        }
    }

    private static MethodInfo ResolveTargetMethod(Type interfaceType, Type implementationType, MethodInfo method)
    {
        var map = implementationType.GetInterfaceMap(interfaceType);
        for (var i = 0; i < map.InterfaceMethods.Length; i++)
        {
            if (map.InterfaceMethods[i] == method)
            {
                return map.TargetMethods[i];
            }
        }

        throw new InvalidOperationException(
            $"Host service implementation '{implementationType.FullName}' does not implement '{interfaceType.FullName}.{method.Name}'.");
    }

    private static object? ReadPropertyValue(Type interfaceType, object implementation, PropertyInfo property)
    {
        var getter = property.GetMethod
            ?? throw new InvalidOperationException($"Host service property '{property.Name}' must have a getter.");
        var targetGetter = ResolveTargetMethod(interfaceType, implementation.GetType(), getter);
        return targetGetter.Invoke(implementation, null);
    }

    private static bool ShouldSkipMethod(MethodInfo method)
        => method.IsSpecialName ||
           method.IsGenericMethodDefinition ||
           IsControlType(method.DeclaringType);

    private static bool ShouldSkipProperty(PropertyInfo property)
        => property.GetMethod is null ||
           property.GetMethod.IsStatic ||
           property.GetIndexParameters().Length != 0 ||
           IsControlType(property.DeclaringType);

    private static bool IsControlType(MemberInfo? type)
        => type is Type t &&
           (string.Equals(t.FullName, ExtensibleControlType, StringComparison.Ordinal) ||
            string.Equals(t.FullName, ServiceControlType, StringComparison.Ordinal));

    private static bool HasDotBoxDServiceAttribute(Type type)
        => type.GetCustomAttributes(inherit: false)
            .Any(attribute => string.Equals(
                attribute.GetType().FullName,
                DotBoxDServiceAttributeType,
                StringComparison.Ordinal));

}
