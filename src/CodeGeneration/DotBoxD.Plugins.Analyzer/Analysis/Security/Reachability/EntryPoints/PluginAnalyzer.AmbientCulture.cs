using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private const string CultureInfoTypeName = "System.Globalization.CultureInfo";
    private const string ClaimsPrincipalTypeName = "System.Security.Claims.ClaimsPrincipal";
    private const string RegexTypeName = "System.Text.RegularExpressions.Regex";

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
                    ReportForbiddenHostStateMutation(context, type);
                }

                break;
            case IFieldSymbol or IPropertySymbol:
                helperGraph.RecordForbiddenInitializer(context.ContainingSymbol, type);
                if (IsEventKernel(context.ContainingSymbol.ContainingType))
                {
                    ReportForbiddenHostStateMutation(context, type);
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

    private static void ReportForbiddenHostStateMutation(
        OperationAnalysisContext context,
        ITypeSymbol type)
        => context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            context.Operation.Syntax.GetLocation(),
            type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));

    private static void ReportAndRecordRegexCacheSizeMutation(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IPropertySymbol property,
        bool writesProperty)
    {
        if (!writesProperty ||
            !IsRegexCacheSizeProperty(property))
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
                    ReportForbiddenHostStateMutation(context, type);
                }

                break;
            case IFieldSymbol or IPropertySymbol:
                helperGraph.RecordForbiddenInitializer(context.ContainingSymbol, type);
                if (IsEventKernel(context.ContainingSymbol.ContainingType))
                {
                    ReportForbiddenHostStateMutation(context, type);
                }

                break;
        }
    }

    private static bool IsRegexCacheSizeProperty(IPropertySymbol property)
        => property is
        {
            IsStatic: true,
            SetMethod: not null,
            Name: "CacheSize"
        } &&
           string.Equals(
               property.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
               RegexTypeName,
               StringComparison.Ordinal);

    private static void ReportAndRecordClaimsPrincipalSelectorMutation(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IPropertySymbol property,
        bool writesProperty)
    {
        if (!writesProperty ||
            !IsClaimsPrincipalSelectorProperty(property))
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
                    ReportForbiddenHostStateMutation(context, type);
                }

                break;
            case IFieldSymbol or IPropertySymbol:
                helperGraph.RecordForbiddenInitializer(context.ContainingSymbol, type);
                if (IsEventKernel(context.ContainingSymbol.ContainingType))
                {
                    ReportForbiddenHostStateMutation(context, type);
                }

                break;
        }
    }

    private static bool IsClaimsPrincipalSelectorProperty(IPropertySymbol property)
        => property is
        {
            IsStatic: true,
            SetMethod: not null,
            Name: "ClaimsPrincipalSelector" or "PrimaryIdentitySelector"
        } &&
           string.Equals(
               property.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
               ClaimsPrincipalTypeName,
               StringComparison.Ordinal);
}
