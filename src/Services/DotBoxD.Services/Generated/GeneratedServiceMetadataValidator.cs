using DotBoxD.Services.Server;

namespace DotBoxD.Services.Generated;

internal static class GeneratedServiceMetadataValidator
{
    public static void ValidateForRegistration<TService>(GeneratedService service, string paramName)
        where TService : class
    {
        ValidateServiceShape(service, paramName);
        if (service.ServiceType != typeof(TService))
        {
            throw new ArgumentException(
                $"Generated service metadata describes {FormatType(service.ServiceType)}, " +
                $"but it was registered for {FormatType(typeof(TService))}.",
                paramName);
        }
        ValidateImplementationTypes(service, paramName);
        ValidateMethods(service.Methods, paramName);
    }

    public static void Validate(
        GeneratedService service,
        string paramName,
        bool validateImplementationTypes = true)
    {
        ValidateServiceShape(service, paramName);
        if (validateImplementationTypes)
        {
            ValidateImplementationTypes(service, paramName);
        }
        ValidateMethods(service.Methods, paramName);
    }

    private static void ValidateServiceShape(GeneratedService service, string paramName)
    {
        if (service.ServiceType is null)
        {
            throw new ArgumentException("Generated service metadata must include a service type.", paramName);
        }
        if (service.ProxyType is null)
        {
            throw new ArgumentException("Generated service metadata must include a proxy type.", paramName);
        }
        if (service.DispatcherType is null)
        {
            throw new ArgumentException("Generated service metadata must include a dispatcher type.", paramName);
        }
        if (!service.ServiceType.IsInterface)
        {
            throw new ArgumentException(
                $"Generated service metadata service type must be an interface. Received {FormatType(service.ServiceType)}.",
                paramName);
        }
        if (string.IsNullOrWhiteSpace(service.ServiceName))
        {
            throw new ArgumentException("Generated service metadata must include a service name.", paramName);
        }
    }

    private static void ValidateImplementationTypes(GeneratedService service, string paramName)
    {
        if (!service.ServiceType.IsAssignableFrom(service.ProxyType))
        {
            throw new ArgumentException(
                $"Generated proxy type {FormatType(service.ProxyType)} must implement " +
                $"{FormatType(service.ServiceType)}.",
                paramName);
        }
        if (!typeof(IServiceDispatcher).IsAssignableFrom(service.DispatcherType))
        {
            throw new ArgumentException(
                $"Generated dispatcher type {FormatType(service.DispatcherType)} must implement " +
                $"{FormatType(typeof(IServiceDispatcher))}.",
                paramName);
        }
    }

    private static void ValidateMethods(IReadOnlyList<GeneratedMethod>? methods, string paramName)
    {
        if (methods is null)
        {
            throw new ArgumentException("Generated service metadata must include a methods collection.", paramName);
        }

        for (var i = 0; i < methods.Count; i++)
        {
            ValidateMethod(methods[i], paramName);
        }
    }

    private static void ValidateMethod(GeneratedMethod method, string paramName)
    {
        if (string.IsNullOrWhiteSpace(method.Name))
        {
            throw new ArgumentException("Generated method metadata must include a method name.", paramName);
        }
        if (string.IsNullOrWhiteSpace(method.WireName))
        {
            throw new ArgumentException("Generated method metadata must include a wire name.", paramName);
        }
        if (method.ReturnType is null)
        {
            throw new ArgumentException("Generated method metadata must include a return type.", paramName);
        }
        if (method.ReturnKind is < GeneratedReturnKind.Void or > GeneratedReturnKind.ValueTaskOfPipe)
        {
            throw new ArgumentException("Generated method metadata has an unsupported return kind.", paramName);
        }
        ValidateReturnMetadata(method, paramName);

        ValidateParameters(method.Parameters, paramName);
    }

    private static void ValidateReturnMetadata(GeneratedMethod method, string paramName)
    {
        if (RequiresResultType(method.ReturnKind) && method.ResultType is null)
        {
            throw new ArgumentException(
                $"Generated method metadata for '{method.Name}' must include a result type for {method.ReturnKind}.",
                paramName);
        }

        if (RequiresNestedServiceFlag(method.ReturnKind))
        {
            if (!method.ReturnsNestedService)
            {
                throw new ArgumentException(
                    $"Generated method metadata for '{method.Name}' must mark {method.ReturnKind} as a nested service return.",
                    paramName);
            }

            return;
        }

        if (method.ReturnsNestedService)
        {
            throw new ArgumentException(
                $"Generated method metadata for '{method.Name}' marks non-nested return kind {method.ReturnKind} as nested.",
                paramName);
        }
    }

    private static bool RequiresResultType(GeneratedReturnKind kind) =>
        kind is GeneratedReturnKind.TaskOfT or
            GeneratedReturnKind.ValueTaskOfT or
            GeneratedReturnKind.TaskOfNestedService or
            GeneratedReturnKind.ValueTaskOfNestedService or
            GeneratedReturnKind.TaskOfAsyncEnumerable or
            GeneratedReturnKind.ValueTaskOfAsyncEnumerable or
            GeneratedReturnKind.TaskOfStream or
            GeneratedReturnKind.ValueTaskOfStream or
            GeneratedReturnKind.TaskOfPipe or
            GeneratedReturnKind.ValueTaskOfPipe;

    private static bool RequiresNestedServiceFlag(GeneratedReturnKind kind) =>
        kind is GeneratedReturnKind.SyncNestedService or
            GeneratedReturnKind.TaskOfNestedService or
            GeneratedReturnKind.ValueTaskOfNestedService;

    private static void ValidateParameters(IReadOnlyList<GeneratedParameter>? parameters, string paramName)
    {
        if (parameters is null)
        {
            throw new ArgumentException("Generated method metadata must include a parameters collection.", paramName);
        }

        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            if (string.IsNullOrWhiteSpace(parameter.Name))
            {
                throw new ArgumentException("Generated parameter metadata must include a parameter name.", paramName);
            }
            if (parameter.Type is null)
            {
                throw new ArgumentException("Generated parameter metadata must include a parameter type.", paramName);
            }
            if (parameter.Position != i)
            {
                throw new ArgumentException("Generated parameter metadata positions must be zero-based and ordered.", paramName);
            }
            ValidateParameterShape(parameter, paramName);
            for (var j = 0; j < i; j++)
            {
                if (string.Equals(parameters[j].Name, parameter.Name, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Generated method metadata contains duplicate parameter name '{parameter.Name}'.",
                        paramName);
                }
            }
        }
    }

    private static void ValidateParameterShape(GeneratedParameter parameter, string paramName)
    {
        var isCancellationTokenType = parameter.Type == typeof(CancellationToken);
        if (parameter.IsCancellationToken != isCancellationTokenType)
        {
            throw new ArgumentException(
                "Generated parameter metadata cancellation-token flag must match its parameter type.",
                paramName);
        }

        if (parameter.IsCancellationToken && parameter.DefaultValue is not null)
        {
            throw new ArgumentException(
                "Generated cancellation-token parameter metadata must not carry a default value object.",
                paramName);
        }

        if (!parameter.HasDefaultValue && parameter.DefaultValue is not null)
        {
            throw new ArgumentException(
                "Generated parameter metadata must not carry a default value when HasDefaultValue is false.",
                paramName);
        }
    }

    private static string FormatType(Type type) => type.FullName ?? type.Name;
}
