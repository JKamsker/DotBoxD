using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!IsEventKernel(type))
        {
            return;
        }

        foreach (var attribute in type.GetAttributes())
        {
            if (TryGetForbiddenAttributeMetadataType(attribute, out var forbiddenType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ForbiddenHostApiRule,
                    AttributeLocation(attribute, type, context.CancellationToken),
                    forbiddenType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                return;
            }
        }
    }

    private static bool TryGetForbiddenAttributeMetadataType(AttributeData attribute, out ITypeSymbol forbiddenType)
    {
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (TryGetForbiddenAttributeMetadataType(argument, out forbiddenType))
            {
                return true;
            }
        }

        foreach (var argument in attribute.NamedArguments)
        {
            if (TryGetForbiddenAttributeMetadataType(argument.Value, out forbiddenType))
            {
                return true;
            }
        }

        forbiddenType = null!;
        return false;
    }

    private static bool TryGetForbiddenAttributeMetadataType(TypedConstant constant, out ITypeSymbol forbiddenType)
    {
        if (constant.Kind == TypedConstantKind.Type &&
            constant.Value is ITypeSymbol type)
        {
            return TryGetForbiddenHostApi(type, out forbiddenType);
        }

        if (constant.Kind == TypedConstantKind.Array)
        {
            foreach (var value in constant.Values)
            {
                if (TryGetForbiddenAttributeMetadataType(value, out forbiddenType))
                {
                    return true;
                }
            }
        }

        forbiddenType = null!;
        return false;
    }

    private static Location? AttributeLocation(
        AttributeData attribute,
        INamedTypeSymbol attributedType,
        CancellationToken cancellationToken)
        => attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
            ?? attributedType.Locations.FirstOrDefault();
}
