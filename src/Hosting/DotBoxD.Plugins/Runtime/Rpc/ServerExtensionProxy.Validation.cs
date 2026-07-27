using System.Reflection;

namespace DotBoxD.Plugins.Runtime.Rpc;

internal static class ServerExtensionProxyValidation
{
    public static void ValidateServiceContract(Type serviceType)
    {
        if (!serviceType.IsInterface)
        {
            throw new NotSupportedException("Server extension proxy service type must be an interface.");
        }

        var methods = ContractMethods(serviceType).ToArray();
        if (methods.Any(static method => method.IsSpecialName))
        {
            throw new NotSupportedException(
                "Server extension proxy service type must declare exactly one ordinary method.");
        }

        if (methods.Length != 1)
        {
            throw new NotSupportedException(
                "Server extension proxy service type must declare exactly one method.");
        }

        foreach (var method in methods)
        {
            ValidateServiceMethod(method);
        }
    }

    public static void ValidatePayloadType(Type type)
    {
        if (IsTaskLike(type))
        {
            throw new NotSupportedException(
                $"Server extension proxy task-like payload type '{type}' is not supported; " +
                "Task and ValueTask are only supported as top-level return types.");
        }

        KernelRpcMarshaller.RejectUnsupportedNullableValueTypesForServerExtension(type);
        _ = KernelRpcMarshaller.SandboxTypeOf(type);
    }

    public static void RejectNullReferenceDefault(ParameterInfo parameter)
    {
        if (parameter.HasDefaultValue &&
            parameter.DefaultValue is null &&
            !parameter.ParameterType.IsValueType)
        {
            throw new NotSupportedException(
                $"Server extension service parameter '{parameter.Name}' cannot default to null because kernel RPC does not encode null reference values.");
        }
    }

    private static void ValidateServiceMethod(MethodInfo method)
    {
        if (method.IsGenericMethodDefinition || method.ContainsGenericParameters)
        {
            throw new NotSupportedException("Server extension proxy service methods must be non-generic.");
        }

        var parameters = method.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            ValidateServiceParameter(parameters, i);
        }

        if (UnwrapReturnType(method.ReturnType) is { } payloadType)
        {
            ValidatePayloadType(payloadType);
        }
    }

    private static void ValidateServiceParameter(ParameterInfo[] parameters, int index)
    {
        var parameter = parameters[index];
        var parameterType = parameter.ParameterType;
        if (parameterType == typeof(CancellationToken))
        {
            if (index != parameters.Length - 1)
            {
                throw new NotSupportedException(
                    "Server extension proxy cancellation tokens must be the final method parameter.");
            }

            return;
        }

        RejectNullReferenceDefault(parameter);
        ValidatePayloadType(parameterType);
    }

    private static IEnumerable<MethodInfo> ContractMethods(Type serviceType)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var methods = serviceType.GetMethods()
            .Concat(serviceType.GetInterfaces().SelectMany(static inherited => inherited.GetMethods()));
        foreach (var method in methods)
        {
            if (seen.Add(ContractMethodKey(method)))
            {
                yield return method;
            }
        }
    }

    private static string ContractMethodKey(MethodInfo method)
        => method.Name + "|" + method.ReturnType.FullName + "|" +
           string.Join("|", method.GetParameters().Select(static parameter => parameter.ParameterType.FullName));

    private static Type? UnwrapReturnType(Type type)
    {
        if (type == typeof(void) || type == typeof(Task) || type == typeof(ValueTask))
        {
            return null;
        }

        if (type.IsGenericType &&
            type.GetGenericTypeDefinition() is { } definition &&
            (definition == typeof(Task<>) || definition == typeof(ValueTask<>)))
        {
            return type.GetGenericArguments()[0];
        }

        return type;
    }

    private static bool IsTaskLike(Type type)
    {
        if (type == typeof(Task) || type == typeof(ValueTask))
        {
            return true;
        }

        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(Task<>) || definition == typeof(ValueTask<>);
    }
}
