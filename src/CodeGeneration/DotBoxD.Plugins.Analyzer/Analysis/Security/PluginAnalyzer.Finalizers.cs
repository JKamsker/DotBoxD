using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void RecordFinalizerReachability(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        ITypeSymbol? type)
    {
        var finalizer = Finalizer(type);
        if (finalizer is null)
        {
            return;
        }

        var location = context.Operation.Syntax.GetLocation();
        if (context.ContainingSymbol is IMethodSymbol method)
        {
            helperGraph.RecordCall(method, finalizer, location);
            return;
        }

        if (context.ContainingSymbol is IFieldSymbol or IPropertySymbol)
        {
            helperGraph.RecordInitializerRootCall(
                context.ContainingSymbol,
                finalizer,
                location);
        }
    }

    private static IMethodSymbol? Finalizer(ITypeSymbol? type)
        => (type as INamedTypeSymbol)?
            .GetMembers()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method => method.MethodKind == MethodKind.Destructor);
}
