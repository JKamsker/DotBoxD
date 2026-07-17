using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeNamedTypeAttributes(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!IsEventKernel(type))
        {
            return;
        }

        foreach (var attribute in type.GetAttributes())
        {
            if (FirstForbiddenAttributeValue(attribute) is { } forbiddenType)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ForbiddenHostApiRule,
                    AttributeLocation(attribute, type, context.CancellationToken),
                    forbiddenType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                return;
            }
        }
    }

    private static Location? AttributeLocation(
        AttributeData attribute,
        INamedTypeSymbol attributedType,
        CancellationToken cancellationToken)
        => attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
            ?? attributedType.Locations.FirstOrDefault();
}
