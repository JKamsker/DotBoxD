using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private const string GetPinnableReferenceMethodName = "GetPinnableReference";

    private static void RegisterFixedReachabilityAnalysis(
        CompilationStartAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
        => context.RegisterSyntaxNodeAction(
            c => AnalyzeFixedStatement(c, helperGraph),
            SyntaxKind.FixedStatement);

    private static void AnalyzeFixedStatement(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is not FixedStatementSyntax fixedStatement ||
            context.SemanticModel.GetEnclosingSymbol(
                fixedStatement.SpanStart,
                context.CancellationToken) is not IMethodSymbol method)
        {
            return;
        }

        var location = fixedStatement.FixedKeyword.GetLocation();
        foreach (var variable in fixedStatement.Declaration.Variables)
        {
            if (variable.Initializer?.Value is not { } expression)
            {
                continue;
            }

            var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
            if (TryResolveGetPinnableReference(type, out var pinnableReference))
            {
                if (TryGetForbiddenHostApi(pinnableReference.ContainingType, out var forbiddenType))
                {
                    helperGraph.RecordForbidden(method, forbiddenType);
                    if (IsEventKernel(method.ContainingType))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            ForbiddenHostApiRule,
                            location,
                            forbiddenType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                    }
                }

                helperGraph.RecordCall(method, pinnableReference, location);
            }
        }
    }

    private static bool TryResolveGetPinnableReference(
        ITypeSymbol? type,
        out IMethodSymbol pinnableReference)
    {
        pinnableReference = null!;
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        for (INamedTypeSymbol? current = namedType; current is not null; current = current.BaseType)
        {
            pinnableReference = current
                .GetMembers(GetPinnableReferenceMethodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(static method =>
                    !method.IsStatic &&
                    method.Parameters.Length == 0 &&
                    method.RefKind != RefKind.None)!;
            if (pinnableReference is not null)
            {
                return true;
            }
        }

        return false;
    }
}
