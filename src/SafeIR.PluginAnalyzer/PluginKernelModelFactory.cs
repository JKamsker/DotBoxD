namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class PluginKernelModelFactory
{
    public static GeneratedPluginPackageResult? Create(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TargetSymbol is not INamedTypeSymbol type ||
            context.TargetNode is not ClassDeclarationSyntax declaration) {
            return null;
        }

        var pluginId = PluginSymbolReader.PluginId(context.Attributes);
        var eventType = PluginSymbolReader.EventType(type);
        if (pluginId is null) {
            return null;
        }

        if (eventType is null)
        {
            var diagnostic = Diagnostic.Create(
                PluginAnalyzerDiagnostics.UnsupportedKernelShapeRule,
                declaration.Identifier.GetLocation(),
                "Game plugins must implement IEventKernel<TEvent>.");
            return new GeneratedPluginPackageResult(null, diagnostic);
        }

        try
        {
            var shouldHandle = InterfaceMethodSyntax(context, type, "ShouldHandle", cancellationToken);
            var handle = InterfaceMethodSyntax(context, type, "Handle", cancellationToken);
            var eventProperties = PluginSymbolReader.EventProperties(eventType);
            if (eventProperties.Any(p => p.Type == "unsupported"))
            {
                throw new NotSupportedException("Kernel event properties must use supported scalar types.");
            }

            var model = new PluginKernelModel(
                PluginId: pluginId,
                Namespace: type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString(),
                KernelName: type.Name,
                PackageName: PackageName(type.Name),
                EventName: eventType.Name,
                EventParameterName: shouldHandle.ParameterList.Parameters.FirstOrDefault()?.Identifier.ValueText ?? "e",
                ContextParameterName: shouldHandle.ParameterList.Parameters.Skip(1).FirstOrDefault()?.Identifier.ValueText ?? "ctx",
                HandleEventParameterName: handle.ParameterList.Parameters.FirstOrDefault()?.Identifier.ValueText ?? "e",
                HandleContextParameterName: handle.ParameterList.Parameters.Skip(1).FirstOrDefault()?.Identifier.ValueText ?? "ctx",
                EventProperties: eventProperties,
                LiveSettings: PluginSymbolReader.LiveSettings(type, context.SemanticModel, cancellationToken),
                ShouldHandle: shouldHandle,
                Handle: handle);
            return new GeneratedPluginPackageResult(SafeIrPackageSourceEmitter.Emit(model), null);
        }
        catch (NotSupportedException ex)
        {
            var diagnostic = Diagnostic.Create(
                PluginAnalyzerDiagnostics.UnsupportedKernelShapeRule,
                declaration.Identifier.GetLocation(),
                ex.Message);
            return new GeneratedPluginPackageResult(null, diagnostic);
        }
    }

    private static MethodDeclarationSyntax InterfaceMethodSyntax(
        GeneratorAttributeSyntaxContext context,
        INamedTypeSymbol type,
        string methodName,
        CancellationToken cancellationToken)
    {
        var interfaceMember = type.AllInterfaces
            .Where(i => string.Equals(
                i.OriginalDefinition.ToDisplayString(),
                "SafeIR.Plugins.IEventKernel<TEvent>",
                StringComparison.Ordinal))
            .SelectMany(i => i.GetMembers(methodName))
            .OfType<IMethodSymbol>()
            .FirstOrDefault();
        if (interfaceMember is null)
        {
            throw new NotSupportedException($"Kernel must implement IEventKernel.{methodName}.");
        }

        var implementation = type.FindImplementationForInterfaceMember(interfaceMember) as IMethodSymbol;
        if (implementation is null)
        {
            throw new NotSupportedException($"Kernel {methodName} implementation could not be resolved.");
        }

        foreach (var reference in implementation.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is MethodDeclarationSyntax method)
            {
                return method;
            }
        }

        throw new NotSupportedException($"Kernel {methodName} must be declared in source.");
    }

    private static string PackageName(string kernelName)
        => kernelName.EndsWith("Kernel", StringComparison.Ordinal)
            ? kernelName.Substring(0, kernelName.Length - "Kernel".Length) + "PluginPackage"
            : kernelName + "PluginPackage";
}
