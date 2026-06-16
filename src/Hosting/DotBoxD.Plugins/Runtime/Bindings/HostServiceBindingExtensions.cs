using System.Reflection;
using DotBoxD.Abstractions;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Hosting.Execution;

public static class HostServiceBindingExtensions
{
    private const string ExtensibleControlType = "DotBoxD.Abstractions.IExtensibleControl";
    private const string ServiceControlType = "DotBoxD.Abstractions.IServiceControl";

    public static SandboxHostBuilder AddBindingsFrom<TService>(
        this SandboxHostBuilder builder,
        TService implementation)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(implementation);

        var visited = new HashSet<Type>();
        AddServiceBindings(builder, typeof(TService), implementation, visited);
        return builder;
    }

    private static void AddServiceBindings(
        SandboxHostBuilder builder,
        Type serviceType,
        object implementation,
        HashSet<Type> visited)
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

            var target = ResolveTargetMethod(serviceType, implementation.GetType(), method);
            var capability = target.GetCustomAttribute<HostCapabilityAttribute>();
            if (capability is null)
            {
                throw new InvalidOperationException(
                    $"Host service method '{serviceType.FullName}.{method.Name}' must declare [HostCapability] on its implementation.");
            }

            builder.AddBinding(CreateBinding(method, target, implementation, capability.Capability));
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

            AddServiceBindings(builder, property.PropertyType, child, visited);
        }
    }

    private static BindingDescriptor CreateBinding(
        MethodInfo interfaceMethod,
        MethodInfo targetMethod,
        object target,
        string capability)
    {
        var payloadType = UnwrapReturnType(interfaceMethod.ReturnType);
        var parameters = interfaceMethod.GetParameters()
            .Select(parameter => KernelRpcMarshaller.SandboxTypeOf(parameter.ParameterType))
            .ToArray();
        var returnType = payloadType is null ? SandboxType.Unit : KernelRpcMarshaller.SandboxTypeOf(payloadType);
        var effects = InferEffects(interfaceMethod, returnType);
        var id = HostBindingRoute(interfaceMethod.DeclaringType!, interfaceMethod);
        var safety = (effects & SandboxEffect.HostStateWrite) != SandboxEffect.None
            ? BindingSafety.SideEffectingExternal
            : BindingSafety.ReadOnlyExternal;

        return new BindingDescriptor(
            id,
            SemVersion.One,
            parameters,
            returnType,
            effects,
            capability,
            BindingCostModel.Fixed(BaseFuel(returnType)),
            AuditLevel.PerResource,
            safety,
            (context, args, cancellationToken) =>
                InvokeAsync(context, args, cancellationToken, id, capability, effects, targetMethod, target, payloadType),
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { });
    }

    private static async ValueTask<SandboxValue> InvokeAsync(
        SandboxContext context,
        IReadOnlyList<SandboxValue> args,
        CancellationToken cancellationToken,
        string bindingId,
        string capability,
        SandboxEffect effects,
        MethodInfo targetMethod,
        object target,
        Type? payloadType)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = DateTimeOffset.UtcNow;
        var values = ConvertArguments(targetMethod, args);
        var result = targetMethod.Invoke(target, values);
        var payload = await AwaitReturnAsync(result, targetMethod.ReturnType).ConfigureAwait(false);
        WriteAudit(context, bindingId, capability, effects, startedAt, values);
        return payloadType is null
            ? SandboxValue.Unit
            : KernelRpcMarshaller.ToSandboxValue(payload, payloadType);
    }

    private static object?[] ConvertArguments(MethodInfo targetMethod, IReadOnlyList<SandboxValue> args)
    {
        var parameters = targetMethod.GetParameters();
        var values = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            values[i] = KernelRpcMarshaller.FromSandboxValue(args[i], parameters[i].ParameterType);
        }

        return values;
    }

    private static async ValueTask<object?> AwaitReturnAsync(object? result, Type returnType)
    {
        if (returnType == typeof(void) || result is null)
        {
            return null;
        }

        if (returnType == typeof(ValueTask))
        {
            await ((ValueTask)result).ConfigureAwait(false);
            return null;
        }

        if (returnType == typeof(Task))
        {
            await ((Task)result).ConfigureAwait(false);
            return null;
        }

        if (IsGenericValueTask(returnType))
        {
            var task = (Task)returnType.GetMethod(nameof(ValueTask<int>.AsTask))!.Invoke(result, null)!;
            await task.ConfigureAwait(false);
            return task.GetType().GetProperty(nameof(Task<int>.Result))!.GetValue(task);
        }

        if (IsGenericTask(returnType))
        {
            var task = (Task)result;
            await task.ConfigureAwait(false);
            return task.GetType().GetProperty(nameof(Task<int>.Result))!.GetValue(task);
        }

        return result;
    }

    private static void WriteAudit(
        SandboxContext context,
        string bindingId,
        string capability,
        SandboxEffect effects,
        DateTimeOffset startedAt,
        IReadOnlyList<object?> values)
    {
        var resourceId = values.Count > 0 && values[0] is string id ? $"entity:{id}" : bindingId;
        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "BindingCall",
            startedAt,
            true,
            BindingId: bindingId,
            CapabilityId: capability,
            Effect: effects & (SandboxEffect.HostStateRead | SandboxEffect.HostStateWrite),
            ResourceId: resourceId,
            Fields: context.BindingAuditFields("host-service", startedAt)));
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

    private static Type? UnwrapReturnType(Type type)
    {
        if (type == typeof(void) || type == typeof(Task) || type == typeof(ValueTask))
        {
            return null;
        }

        if ((IsGenericTask(type) || IsGenericValueTask(type)) && type.GetGenericArguments() is [var payload])
        {
            return payload;
        }

        return type;
    }

    private static bool IsGenericTask(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>);

    private static bool IsGenericValueTask(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>);

    private static SandboxEffect InferEffects(MethodInfo method, SandboxType returnType)
    {
        var effects = SandboxEffect.Cpu;
        if (ReturnAllocates(returnType))
        {
            effects |= SandboxEffect.Alloc;
        }

        return IsWriteMethod(method)
            ? effects | SandboxEffect.HostStateWrite
            : effects | SandboxEffect.HostStateRead;
    }

    private static bool IsWriteMethod(MethodInfo method)
        => method.Name.StartsWith("Kill", StringComparison.Ordinal) ||
           method.Name.StartsWith("Set", StringComparison.Ordinal) ||
           method.Name.StartsWith("Update", StringComparison.Ordinal) ||
           method.Name.StartsWith("Delete", StringComparison.Ordinal) ||
           method.Name.StartsWith("Add", StringComparison.Ordinal) ||
           method.Name.StartsWith("Remove", StringComparison.Ordinal);

    private static bool ReturnAllocates(SandboxType type)
        => type != SandboxType.Unit &&
           type != SandboxType.Bool &&
           type != SandboxType.I32 &&
           type != SandboxType.I64 &&
           type != SandboxType.F64;

    private static long BaseFuel(SandboxType returnType) => ReturnAllocates(returnType) ? 3 : 2;

    private static string HostBindingRoute(Type type, MethodInfo method)
        => "host." + (type.Namespace is null ? type.Name : type.Namespace + "." + type.Name) + "." + method.Name;
}
