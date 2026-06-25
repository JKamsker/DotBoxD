using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcKernelGraftSignatureFactory
{
    public static EquatableArray<RpcKernelGraftSignature> Create(
        INamedTypeSymbol kernelType,
        IMethodSymbol kernelMethod,
        IMethodSymbol? serviceMethod,
        RpcKernelClientExtensions? clientExtensions,
        RpcServerExtensionGraft? graft)
    {
        var items = new List<RpcKernelGraftSignature>(2);
        if (serviceMethod is not null && clientExtensions is not null)
        {
            AddClientExtensions(items, kernelType, serviceMethod, clientExtensions);
        }
        else if (graft is not null &&
                 RpcKernelClientExtensionModelFactory.HasExtensionAttribute(kernelMethod))
        {
            items.Add(MethodSignature(
                kernelType,
                graft.ReceiverType,
                kernelMethod.Name,
                UserParameterTypes(kernelMethod),
                kernelMethod));
        }

        return new EquatableArray<RpcKernelGraftSignature>(items);
    }

    private static void AddClientExtensions(
        List<RpcKernelGraftSignature> items,
        INamedTypeSymbol kernelType,
        IMethodSymbol serviceMethod,
        RpcKernelClientExtensions clientExtensions)
    {
        if (clientExtensions.Property is { } property)
        {
            items.Add(Signature(
                kernelType,
                property.ReceiverType,
                "property",
                property.Name,
                "",
                kernelType));
        }

        if (clientExtensions.Method is { } method)
        {
            items.Add(MethodSignature(
                kernelType,
                method.ReceiverType,
                method.Name,
                ParameterTypes(serviceMethod.Parameters),
                serviceMethod));
        }
    }

    private static RpcKernelGraftSignature MethodSignature(
        INamedTypeSymbol kernelType,
        INamedTypeSymbol receiverType,
        string name,
        string parameters,
        ISymbol locationSymbol)
        => Signature(kernelType, receiverType, "method", name, parameters, locationSymbol);

    private static RpcKernelGraftSignature Signature(
        INamedTypeSymbol kernelType,
        INamedTypeSymbol receiverType,
        string kind,
        string name,
        string parameters,
        ISymbol locationSymbol)
    {
        var ns = kernelType.ContainingNamespace.IsGlobalNamespace
            ? ""
            : kernelType.ContainingNamespace.ToDisplayString();
        var receiver = TypeName(receiverType);
        var display = kind == "method"
            ? name + "(" + parameters + ")"
            : name;
        var key = string.Join("\u001f", ns, receiver, kind, name, parameters);
        return new RpcKernelGraftSignature(
            key,
            ns,
            receiver,
            display,
            kernelType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            DiagnosticLocation(locationSymbol));
    }

    private static string UserParameterTypes(IMethodSymbol method)
        => ParameterTypes(method.Parameters.Take(method.Parameters.Length - 1));

    private static string ParameterTypes(IEnumerable<IParameterSymbol> parameters)
        => string.Join(", ", parameters.Select(parameter => TypeName(parameter.Type)));

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static PluginDiagnosticLocation? DiagnosticLocation(ISymbol symbol)
        => symbol.Locations.Length > 0
            ? PluginDiagnosticLocation.From(symbol.Locations[0])
            : null;
}

internal sealed record RpcKernelGraftSignature(
    string Key,
    string Namespace,
    string ReceiverType,
    string Display,
    string KernelType,
    PluginDiagnosticLocation? Location);
