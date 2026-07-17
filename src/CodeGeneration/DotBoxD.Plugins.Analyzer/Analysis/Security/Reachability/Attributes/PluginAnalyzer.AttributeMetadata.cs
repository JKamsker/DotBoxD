using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void RecordForbiddenAttributeMetadata(
        SymbolAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method)
    {
        if (method.MethodKind is MethodKind.EventAdd or MethodKind.EventRemove)
        {
            return;
        }

        if (FirstForbiddenMethodAttributeMetadata(method, context.CancellationToken) is not { } metadata)
        {
            return;
        }

        if (IsEventKernel(method.ContainingType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ForbiddenHostApiRule,
                metadata.Location,
                metadata.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            return;
        }

        helperGraph.RecordForbidden(method, metadata.Type);
    }

    private static ForbiddenAttributeMetadata? FirstForbiddenMethodAttributeMetadata(
        IMethodSymbol method,
        CancellationToken cancellationToken)
        => FirstForbiddenAttributeMetadata(method.GetAttributes(), cancellationToken) ??
           FirstForbiddenAttributeMetadata(method.GetReturnTypeAttributes(), cancellationToken) ??
           FirstForbiddenParameterAttributeMetadata(method.Parameters, cancellationToken);

    private static ForbiddenAttributeMetadata? FirstForbiddenParameterAttributeMetadata(
        ImmutableArray<IParameterSymbol> parameters,
        CancellationToken cancellationToken)
    {
        foreach (var parameter in parameters)
        {
            if (FirstForbiddenAttributeMetadata(parameter.GetAttributes(), cancellationToken) is { } metadata)
            {
                return metadata;
            }
        }

        return null;
    }

    private static ForbiddenAttributeMetadata? FirstForbiddenAttributeMetadata(
        IEnumerable<AttributeData> attributes,
        CancellationToken cancellationToken)
    {
        foreach (var attribute in attributes)
        {
            if (FirstForbiddenAttributeValue(attribute) is { } type)
            {
                return new ForbiddenAttributeMetadata(type, AttributeLocation(attribute, cancellationToken));
            }
        }

        return null;
    }

    private static ITypeSymbol? FirstForbiddenAttributeValue(AttributeData attribute)
    {
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (FirstForbiddenAttributeValue(argument) is { } type)
            {
                return type;
            }
        }

        foreach (var argument in attribute.NamedArguments)
        {
            if (FirstForbiddenAttributeValue(argument.Value) is { } type)
            {
                return type;
            }
        }

        return null;
    }

    private static ITypeSymbol? FirstForbiddenAttributeValue(TypedConstant argument)
    {
        if (argument.Kind == TypedConstantKind.Type &&
            argument.Value is ITypeSymbol type)
        {
            return FirstForbiddenHostApi(type);
        }

        // Roslyn uses a default Values array for null array arguments such as [InlineData(null)].
        if (argument.Kind != TypedConstantKind.Array ||
            argument.IsNull ||
            argument.Values.IsDefault)
        {
            return null;
        }

        foreach (var element in argument.Values)
        {
            if (FirstForbiddenAttributeValue(element) is { } elementType)
            {
                return elementType;
            }
        }

        return null;
    }

    private static Location? AttributeLocation(AttributeData attribute, CancellationToken cancellationToken)
        => attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();

    private readonly record struct ForbiddenAttributeMetadata(ITypeSymbol Type, Location? Location);
}
