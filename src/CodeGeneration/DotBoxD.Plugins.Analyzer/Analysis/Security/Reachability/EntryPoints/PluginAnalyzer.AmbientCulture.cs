using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private const string CultureInfoTypeName = "System.Globalization.CultureInfo";

    private static void ReportAndRecordAmbientCultureMutation(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IPropertySymbol property,
        bool writesProperty)
    {
        if (!writesProperty ||
            !IsAmbientCultureProperty(property))
        {
            return;
        }

        var type = property.ContainingType;
        switch (context.ContainingSymbol)
        {
            case IMethodSymbol method:
                helperGraph.RecordForbidden(method, type);
                if (IsForbiddenApiRoot(context, method))
                {
                    ReportForbiddenAmbientCultureMutation(context, type);
                }

                break;
            case IFieldSymbol or IPropertySymbol:
                helperGraph.RecordForbiddenInitializer(context.ContainingSymbol, type);
                if (IsEventKernel(context.ContainingSymbol.ContainingType))
                {
                    ReportForbiddenAmbientCultureMutation(context, type);
                }

                break;
        }
    }

    private static bool IsAmbientCultureProperty(IPropertySymbol property)
        => property is
        {
            IsStatic: true,
            SetMethod: not null,
            Name: "CurrentCulture" or "DefaultThreadCurrentCulture"
        } &&
           string.Equals(
               property.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
               CultureInfoTypeName,
               StringComparison.Ordinal);

    private static void ReportForbiddenAmbientCultureMutation(
        OperationAnalysisContext context,
        ITypeSymbol type)
        => context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            context.Operation.Syntax.GetLocation(),
            type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
}
