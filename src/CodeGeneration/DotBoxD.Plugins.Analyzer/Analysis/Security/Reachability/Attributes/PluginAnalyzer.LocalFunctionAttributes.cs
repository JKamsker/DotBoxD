using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void RegisterLocalFunctionAttributeAnalysis(
        CompilationStartAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
        => context.RegisterSyntaxNodeAction(
            c => AnalyzeLocalFunctionAttributes(c, helperGraph),
            SyntaxKind.LocalFunctionStatement);

    private static void AnalyzeLocalFunctionAttributes(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is not LocalFunctionStatementSyntax localFunction ||
            context.SemanticModel.GetDeclaredSymbol(localFunction, context.CancellationToken) is not { } method)
        {
            return;
        }

        foreach (var attribute in MethodAndParameterAttributes(method))
        {
            if (FirstForbiddenAttributeValue(attribute) is not { } forbiddenType)
            {
                continue;
            }

            helperGraph.RecordForbidden(method, forbiddenType);
            if (!IsEventKernel(method.ContainingType))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                ForbiddenHostApiRule,
                AttributeLocation(attribute, context.CancellationToken) ?? localFunction.Identifier.GetLocation(),
                forbiddenType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }
    }
}
