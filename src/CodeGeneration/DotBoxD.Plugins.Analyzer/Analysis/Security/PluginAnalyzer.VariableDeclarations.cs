using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeVariableDeclaration(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            return;
        }

        var declaration = (IVariableDeclarationOperation)context.Operation;
        foreach (var declarator in declaration.Declarators)
        {
            RecordDynamicLocalInitializer(helperGraph, declarator);
            if (!TryGetForbiddenHostApi(declarator.Symbol.Type, out var forbidden))
            {
                continue;
            }

            helperGraph.RecordForbidden(method, forbidden);
            if (!IsEventKernel(method.ContainingType) ||
                !helperGraph.TryRecordDirectDiagnostic(method))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                ForbiddenHostApiRule,
                declarator.Syntax.GetLocation(),
                forbidden.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }
    }

    private static void RecordDynamicLocalInitializer(
        ForbiddenHelperCallGraph helperGraph,
        IVariableDeclaratorOperation declarator)
    {
        if (declarator.Symbol.Type.TypeKind != TypeKind.Dynamic)
        {
            return;
        }

        helperGraph.RecordDynamicLocalType(
            declarator.Symbol,
            DynamicInitializerType(declarator.Initializer?.Value));
    }

    private static ITypeSymbol? DynamicInitializerType(IOperation? initializer)
        => initializer switch
        {
            IConversionOperation conversion => DynamicInitializerType(conversion.Operand),
            { Type.TypeKind: not TypeKind.Dynamic } operation => operation.Type,
            _ => null
        };

    private static bool TryGetForbiddenHostApi(
        ITypeSymbol? type,
        out ITypeSymbol forbidden)
    {
        if (TryGetDirectForbiddenHostApi(type, out forbidden))
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return TryGetForbiddenHostApi(array.ElementType, out forbidden);
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (var argument in named.TypeArguments)
            {
                if (TryGetForbiddenHostApi(argument, out forbidden))
                {
                    return true;
                }
            }
        }

        forbidden = null!;
        return false;
    }

    private static bool TryGetDirectForbiddenHostApi(
        ITypeSymbol? type,
        out ITypeSymbol forbidden)
    {
        var name = type?.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (!string.IsNullOrWhiteSpace(name) &&
            (IsForbiddenExactType(name!) || IsForbiddenNamespace(name!)))
        {
            forbidden = type!;
            return true;
        }

        forbidden = null!;
        return false;
    }
}
