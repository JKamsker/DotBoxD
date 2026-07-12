using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void RecordStaticConstructorReachability(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        ISymbol member)
    {
        var containingType = member switch
        {
            IMethodSymbol { IsStatic: true } method => method.ContainingType,
            IPropertySymbol { IsStatic: true } property => property.ContainingType,
            IFieldSymbol { IsStatic: true } field => field.ContainingType,
            _ => null
        };

        RecordStaticConstructorReachability(context, helperGraph, containingType);
    }

    private static void RecordStaticConstructorReachability(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        ITypeSymbol? type)
    {
        var staticConstructor = StaticConstructor(type);
        if (staticConstructor is null)
        {
            return;
        }

        var location = context.Operation.Syntax.GetLocation();
        if (context.ContainingSymbol is IMethodSymbol method)
        {
            helperGraph.RecordCall(method, staticConstructor, location);
            return;
        }

        if (context.ContainingSymbol is IFieldSymbol or IPropertySymbol)
        {
            helperGraph.RecordInitializerRootCall(
                context.ContainingSymbol.ContainingType,
                staticConstructor,
                location);
        }
    }

    private static IMethodSymbol? StaticConstructor(ITypeSymbol? type)
        => (type as INamedTypeSymbol)?
            .GetMembers()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method => method.MethodKind == MethodKind.StaticConstructor);
}
