using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class ServerExtensionAttributeNullDiagnosticFactory
{
    public static bool IsCandidate(SyntaxNode node)
        => node is AttributeSyntax { ArgumentList.Arguments.Count: > 0 } attribute &&
           attribute.Name.ToString().Contains("ServerExtension", StringComparison.Ordinal);

    public static PluginKernelDiagnostic? Create(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var attribute = (AttributeSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(attribute, cancellationToken).Symbol is not IMethodSymbol constructor ||
            !IsServerExtensionAttribute(constructor, context.SemanticModel.Compilation))
        {
            return null;
        }

        var arguments = attribute.ArgumentList?.Arguments;
        if (arguments is null)
        {
            return null;
        }

        for (var i = 0; i < arguments.Value.Count; i++)
        {
            var argument = arguments.Value[i];
            if (!argument.Expression.IsKind(SyntaxKind.NullLiteralExpression))
            {
                continue;
            }

            var parameterName = ParameterName(argument, constructor, i);
            if (parameterName is "id" or "serviceType")
            {
                return new PluginKernelDiagnostic(
                    $"ServerExtension attribute parameter '{parameterName}' cannot be null.",
                    PluginDiagnosticLocation.From(argument.GetLocation()));
            }
        }

        return null;
    }

    private static bool IsServerExtensionAttribute(IMethodSymbol constructor, Compilation compilation)
        => compilation.GetTypeByMetadataName(DotBoxDMetadataNames.ServerExtensionAttribute) is { } expected &&
           SymbolEqualityComparer.Default.Equals(constructor.ContainingType, expected);

    private static string? ParameterName(
        AttributeArgumentSyntax argument,
        IMethodSymbol constructor,
        int ordinal)
    {
        var parameter = ResolveParameter(argument, constructor, ordinal);
        return parameter is { IsOptional: false } ? parameter.Name : null;
    }

    private static IParameterSymbol? ResolveParameter(
        AttributeArgumentSyntax argument,
        IMethodSymbol constructor,
        int ordinal)
    {
        if (argument.NameColon is { Name.Identifier.ValueText: { Length: > 0 } name })
        {
            foreach (var parameter in constructor.Parameters)
            {
                if (parameter.Name == name)
                {
                    return parameter;
                }
            }

            return null;
        }

        return ordinal < constructor.Parameters.Length ? constructor.Parameters[ordinal] : null;
    }
}
