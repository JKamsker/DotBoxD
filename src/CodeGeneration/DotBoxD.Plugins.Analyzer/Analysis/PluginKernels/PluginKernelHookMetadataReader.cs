using DotBoxD.Plugins.Analyzer.Analysis.HookResults;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class PluginKernelHookMetadataReader
{
    public static PluginKernelHookMetadata Read(
        INamedTypeSymbol eventType,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        foreach (var attribute in eventType.GetAttributes())
        {
            if (!EventTypeName.TryHookName(attribute, out var hookName))
            {
                continue;
            }

            return HookMetadata(eventType, attribute, hookName, compilation, cancellationToken);
        }

        return new PluginKernelHookMetadata(EventTypeName.Qualified(eventType), null);
    }

    private static PluginKernelHookMetadata HookMetadata(
        INamedTypeSymbol eventType,
        AttributeData attribute,
        string hookName,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (attribute.ConstructorArguments.Length != 2 ||
            attribute.ConstructorArguments[1].Value is not INamedTypeSymbol resultType ||
            HookResultModelFactory.CanSatisfyHookResult(resultType, compilation, cancellationToken))
        {
            return new PluginKernelHookMetadata(hookName, null);
        }

        var message =
            $"HookAttribute.ResultType '{resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' " +
            "must satisfy the hook-result contract: use a non-generic value type that implements IHookResult, " +
            "or a supported [HookResult] partial record struct with 'bool Success' and 'string? Reason' fields.";
        return new PluginKernelHookMetadata(
            hookName,
            new PluginKernelDiagnostic(
                message,
                EventTypeLocation(eventType, cancellationToken),
                UseHookResultContractRule: true));
    }

    private static PluginDiagnosticLocation? EventTypeLocation(
        INamedTypeSymbol eventType,
        CancellationToken cancellationToken)
    {
        foreach (var reference in eventType.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration)
            {
                return PluginDiagnosticLocation.From(declaration.Identifier.GetLocation());
            }
        }

        return null;
    }
}

internal readonly record struct PluginKernelHookMetadata(
    string EventName,
    PluginKernelDiagnostic? Diagnostic);
