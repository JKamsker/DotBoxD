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
            if (TryResolveGetPinnableReference(
                    context.SemanticModel,
                    expression,
                    type,
                    out var pinnableReference))
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
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        ITypeSymbol? type,
        out IMethodSymbol pinnableReference)
    {
        pinnableReference = null!;
        if (type is INamedTypeSymbol namedType)
        {
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
        }

        var invocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.ParenthesizedExpression(expression.WithoutTrivia()),
                SyntaxFactory.IdentifierName(GetPinnableReferenceMethodName)));
        var method = semanticModel.GetSpeculativeSymbolInfo(
            expression.SpanStart,
            invocation,
            SpeculativeBindingOption.BindAsExpression).Symbol as IMethodSymbol;
        if (method is { MethodKind: MethodKind.ReducedExtension, Parameters.Length: 0, RefKind: not RefKind.None })
        {
            pinnableReference = method.ReducedFrom ?? method;
            return true;
        }

        return false;
    }
}
