using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookResults;

internal static class HookFireAsyncModelFactory
{
    public static HookFireAsyncModelResult? Create(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TargetSymbol is not INamedTypeSymbol contextType ||
            context.TargetNode is not TypeDeclarationSyntax declaration ||
            contextType.TypeParameters.Length > 0)
        {
            return null;
        }

        foreach (var attribute in context.Attributes)
        {
            if (!TryGetHookResultType(attribute, context.SemanticModel.Compilation, cancellationToken, out var resultType))
            {
                continue;
            }

            if (IsFileLocalOrNestedInFileLocal(contextType, cancellationToken))
            {
                return new HookFireAsyncModelResult(
                    null,
                    PluginKernelDiagnostic.Create(
                        declaration.Identifier,
                        $"hook context '{contextType.Name}' is file-local and cannot be referenced by generated "
                        + "HookRegistry.FireAsync(context) extensions; use a non-file-local context type or call "
                        + "HookRegistry.FireAsync<TContext, TResult>(...) from the same file"));
            }

            if (IsErrorObsoleteOrNestedInErrorObsolete(contextType))
            {
                return new HookFireAsyncModelResult(
                    null,
                    PluginKernelDiagnostic.Create(
                        declaration.Identifier,
                        $"hook context '{contextType.Name}' is marked [Obsolete(..., error: true)] or nested in a "
                        + "type marked that way; generated HookRegistry.FireAsync(context) extensions cannot "
                        + "reference compiler-error obsolete context types"));
            }

            return new HookFireAsyncModelResult(
                new HookFireAsyncModel(
                    contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ContextAttributes(contextType),
                    IsEffectivelyPublic(contextType) && IsEffectivelyPublic(resultType) ? "public" : "internal",
                    IsAssemblyClsCompliant(context.SemanticModel.Compilation)),
                null);
        }

        return null;
    }

    private static bool TryGetHookResultType(
        AttributeData attribute,
        Compilation compilation,
        CancellationToken cancellationToken,
        out INamedTypeSymbol resultType)
    {
        resultType = null!;
        if (!string.Equals(
                attribute.AttributeClass?.ToDisplayString(),
                DotBoxDMetadataNames.HookAttribute,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (attribute.ConstructorArguments.Length != 2)
        {
            return false;
        }

        if (attribute.ConstructorArguments[1].Value is not INamedTypeSymbol candidate)
        {
            return false;
        }

        if (!HookResultModelFactory.CanSatisfyHookResult(candidate, compilation, cancellationToken))
        {
            return false;
        }

        resultType = candidate;
        return true;
    }

    private static bool IsFileLocalOrNestedInFileLocal(
        INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            foreach (var reference in current.DeclaringSyntaxReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration &&
                    declaration.Modifiers.Any(SyntaxKind.FileKeyword))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsErrorObsoleteOrNestedInErrorObsolete(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (HasErrorObsoleteAttribute(current.GetAttributes()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasErrorObsoleteAttribute(IEnumerable<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.AttributeClass?.ToDisplayString() == "System.ObsoleteAttribute" &&
                attribute.ConstructorArguments.Length >= 2 &&
                attribute.ConstructorArguments[1].Value is true)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEffectivelyPublic(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAssemblyClsCompliant(Compilation compilation)
    {
        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) !=
                "global::System.CLSCompliantAttribute")
            {
                continue;
            }

            return attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is true;
        }

        return false;
    }

    private static EquatableArray<string> ContextAttributes(INamedTypeSymbol contextType)
    {
        var attributes = new List<string>();
        foreach (var attribute in contextType.GetAttributes())
        {
            if (ExperimentalAttribute(attribute) is { } experimentalAttribute)
            {
                attributes.Add(experimentalAttribute);
            }
        }

        attributes.Sort(StringComparer.Ordinal);
        return new EquatableArray<string>(attributes);
    }

    private static string? ExperimentalAttribute(AttributeData attribute)
    {
        if (attribute.AttributeClass?.ToDisplayString() !=
            DotBoxDMetadataNames.ExperimentalAttribute ||
            attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value is not string diagnosticId)
        {
            return null;
        }

        var arguments = new List<string> { LiteralReader.StringLiteral(diagnosticId) };
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument is { Key: "Message", Value.Value: string message })
            {
                arguments.Add("Message = " + LiteralReader.StringLiteral(message));
            }
            else if (argument is { Key: "UrlFormat", Value.Value: string urlFormat })
            {
                arguments.Add("UrlFormat = " + LiteralReader.StringLiteral(urlFormat));
            }
        }

        return "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(" +
            string.Join(", ", arguments) + ")]";
    }
}
