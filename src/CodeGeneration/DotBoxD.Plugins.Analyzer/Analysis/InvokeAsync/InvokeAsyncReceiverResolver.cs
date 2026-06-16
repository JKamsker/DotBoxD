using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncReceiverResolver
{
    private const string BuilderSuffix = "Builder";

    public static bool TryResolve(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType,
        out INamedTypeSymbol worldType)
    {
        receiverType = null!;
        worldType = null!;

        if (model.GetTypeInfo(receiver, cancellationToken).Type is INamedTypeSymbol semanticType &&
            TryResolveWorld(semanticType, out worldType))
        {
            receiverType = semanticType;
            return true;
        }

        if (TryResolveGeneratedBuilderLocal(model, receiver, cancellationToken, out var generatedType) &&
            TryResolveWorld(generatedType, out worldType))
        {
            receiverType = generatedType;
            return true;
        }

        return false;
    }

    private static bool TryResolveGeneratedBuilderLocal(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        if (receiver is not IdentifierNameSyntax identifier ||
            model.GetSymbolInfo(identifier, cancellationToken).Symbol is not ILocalSymbol local)
        {
            return false;
        }

        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax
                {
                    Initializer.Value: { } initializer
                } &&
                TryFacadeNameFromBuilderInitializer(initializer, out var facadeName) &&
                TryFindGeneratedFacade(model.Compilation, facadeName, cancellationToken, out receiverType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFacadeNameFromBuilderInitializer(
        ExpressionSyntax initializer,
        out string facadeName)
    {
        facadeName = string.Empty;
        return initializer is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Build",
                Expression: { } buildReceiver
            }
        } && TryFacadeNameFromBuilderFactory(buildReceiver, out facadeName);
    }

    private static bool TryFacadeNameFromBuilderFactory(
        ExpressionSyntax buildReceiver,
        out string facadeName)
    {
        facadeName = string.Empty;
        return buildReceiver is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Expression: { } builderType
            }
        } && TryFacadeNameFromBuilderType(builderType, out facadeName);
    }

    private static bool TryFacadeNameFromBuilderType(
        ExpressionSyntax builderType,
        out string facadeName)
    {
        var builderName = builderType switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => string.Empty
        };

        if (!builderName.EndsWith(BuilderSuffix, StringComparison.Ordinal) ||
            builderName.Length == BuilderSuffix.Length)
        {
            facadeName = string.Empty;
            return false;
        }

        facadeName = builderName.Substring(0, builderName.Length - BuilderSuffix.Length);
        return true;
    }

    private static bool TryFindGeneratedFacade(
        Compilation compilation,
        string facadeName,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        foreach (var symbol in compilation.GetSymbolsWithName(
                     name => string.Equals(name, facadeName, StringComparison.Ordinal),
                     SymbolFilter.Type,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (symbol is INamedTypeSymbol candidate &&
                HasGeneratePluginServerAttribute(candidate))
            {
                receiverType = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveWorld(
        INamedTypeSymbol type,
        out INamedTypeSymbol worldType)
    {
        worldType = null!;
        if (!HasGeneratePluginServerAttribute(type))
        {
            return false;
        }

        foreach (var candidate in type.Interfaces)
        {
            if (HasDotBoxDServiceAttribute(candidate))
            {
                worldType = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool HasGeneratePluginServerAttribute(INamedTypeSymbol type)
        => HasAttribute(type, DotBoxDGenerationNames.Metadata.GeneratePluginServerAttribute);

    private static bool HasDotBoxDServiceAttribute(INamedTypeSymbol type)
        => HasAttribute(type, DotBoxDGenerationNames.Metadata.DotBoxDServiceAttribute);

    private static bool HasAttribute(INamedTypeSymbol type, string metadataName)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    metadataName,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
