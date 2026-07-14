using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void RegisterForbiddenTypeSyntaxAnalysis(
        CompilationStartAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        context.RegisterSyntaxNodeAction(
            c => AnalyzeDeclarationPatternType(c, helperGraph),
            SyntaxKind.DeclarationPattern);
        context.RegisterSyntaxNodeAction(
            c => AnalyzeTypePatternType(c, helperGraph),
            SyntaxKind.TypePattern);
        context.RegisterSyntaxNodeAction(
            c => AnalyzeRecursivePatternType(c, helperGraph),
            SyntaxKind.RecursivePattern);
        context.RegisterSyntaxNodeAction(
            c => AnalyzeIsExpressionType(c, helperGraph),
            SyntaxKind.IsExpression);
        context.RegisterSyntaxNodeAction(
            c => AnalyzeAsExpressionType(c, helperGraph),
            SyntaxKind.AsExpression);
        context.RegisterSyntaxNodeAction(
            c => AnalyzeCastExpressionType(c, helperGraph),
            SyntaxKind.CastExpression);
        context.RegisterSyntaxNodeAction(
            c => AnalyzeDefaultExpressionType(c, helperGraph),
            SyntaxKind.DefaultExpression);
        context.RegisterSyntaxNodeAction(
            c => AnalyzeArrayCreationType(c, helperGraph),
            SyntaxKind.ArrayCreationExpression);
        context.RegisterSyntaxNodeAction(
            c => AnalyzeCatchDeclarationType(c, helperGraph),
            SyntaxKind.CatchDeclaration);
        context.RegisterSyntaxNodeAction(
            c => AnalyzeConstantPatternType(c, helperGraph),
            SyntaxKind.ConstantPattern);
        context.RegisterSyntaxNodeAction(
            AnalyzeBaseListType,
            SyntaxKind.SimpleBaseType,
            SyntaxKind.PrimaryConstructorBaseType);
        context.RegisterSyntaxNodeAction(
            AnalyzeTypeParameterConstraintType,
            SyntaxKind.TypeConstraint);
        context.RegisterSyntaxNodeAction(
            c => AnalyzeNameOfArgumentType(c, helperGraph),
            SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(
            c => AnalyzeLocalFunctionSignature(c, helperGraph),
            SyntaxKind.LocalFunctionStatement);
        context.RegisterSyntaxNodeAction(
            c => AnalyzeAnonymousFunctionAttributeTypeReferences(c, helperGraph),
            SyntaxKind.ParenthesizedLambdaExpression,
            SyntaxKind.SimpleLambdaExpression);
    }

    private static void AnalyzeDeclarationPatternType(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is DeclarationPatternSyntax { Type: { } typeSyntax })
        {
            AnalyzeForbiddenTypeSyntax(context, helperGraph, typeSyntax);
        }
    }

    private static void AnalyzeTypePatternType(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is TypePatternSyntax { Type: { } typeSyntax })
        {
            AnalyzeForbiddenTypeSyntax(context, helperGraph, typeSyntax);
        }
    }

    private static void AnalyzeRecursivePatternType(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is RecursivePatternSyntax { Type: { } typeSyntax })
        {
            AnalyzeForbiddenTypeSyntax(context, helperGraph, typeSyntax);
        }
    }

    private static void AnalyzeIsExpressionType(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is BinaryExpressionSyntax { Right: TypeSyntax typeSyntax })
        {
            AnalyzeForbiddenTypeSyntax(context, helperGraph, typeSyntax);
        }
    }

    private static void AnalyzeAsExpressionType(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is BinaryExpressionSyntax { Right: TypeSyntax typeSyntax })
        {
            AnalyzeForbiddenTypeSyntax(context, helperGraph, typeSyntax);
        }
    }

    private static void AnalyzeCastExpressionType(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is CastExpressionSyntax { Type: { } typeSyntax })
        {
            AnalyzeForbiddenTypeSyntax(context, helperGraph, typeSyntax);
        }
    }

    private static void AnalyzeDefaultExpressionType(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is DefaultExpressionSyntax { Type: { } typeSyntax })
        {
            AnalyzeForbiddenTypeSyntax(context, helperGraph, typeSyntax);
        }
    }

    private static void AnalyzeArrayCreationType(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is ArrayCreationExpressionSyntax { Type: { } typeSyntax })
        {
            AnalyzeForbiddenTypeSyntax(context, helperGraph, typeSyntax);
        }
    }

    private static void AnalyzeCatchDeclarationType(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is CatchDeclarationSyntax { Type: { } typeSyntax })
        {
            AnalyzeForbiddenTypeSyntax(context, helperGraph, typeSyntax);
        }
    }

    private static void AnalyzeConstantPatternType(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is not ConstantPatternSyntax { Expression: { } expression })
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
        if (symbol is ITypeSymbol type)
        {
            AnalyzeForbiddenTypeSymbol(context, helperGraph, expression, type);
        }
    }

    private static void AnalyzeBaseListType(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is BaseTypeSyntax { Type: { } typeSyntax })
        {
            AnalyzeForbiddenDeclarationTypeSyntax(context, typeSyntax);
        }
    }

    private static void AnalyzeTypeParameterConstraintType(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is TypeConstraintSyntax { Type: { } typeSyntax })
        {
            AnalyzeForbiddenDeclarationTypeSyntax(context, typeSyntax);
        }
    }

    private static void AnalyzeForbiddenDeclarationTypeSyntax(
        SyntaxNodeAnalysisContext context,
        TypeSyntax typeSyntax)
    {
        var typeDeclaration = typeSyntax.FirstAncestorOrSelf<BaseTypeDeclarationSyntax>();
        if (typeDeclaration is null ||
            context.SemanticModel.GetDeclaredSymbol(
                typeDeclaration,
                context.CancellationToken) is not INamedTypeSymbol declaredType ||
            !IsEventKernel(declaredType))
        {
            return;
        }

        if (context.SemanticModel.GetTypeInfo(typeSyntax, context.CancellationToken).Type is not { } type)
        {
            return;
        }

        ReportForbiddenType(context.ReportDiagnostic, type, typeSyntax.GetLocation());
    }

    private static void AnalyzeNameOfArgumentType(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is not InvocationExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
                ArgumentList.Arguments.Count: 1
            } invocation)
        {
            return;
        }

        var expression = invocation.ArgumentList.Arguments[0].Expression;
        var symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
        if (symbol is ITypeSymbol type)
        {
            AnalyzeForbiddenTypeSymbol(context, helperGraph, expression, type);
        }
    }

    private static void AnalyzeLocalFunctionSignature(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is not LocalFunctionStatementSyntax localFunction)
        {
            return;
        }

        AnalyzeForbiddenTypeSyntax(context, helperGraph, localFunction.ReturnType);
        foreach (var parameter in localFunction.ParameterList.Parameters)
        {
            if (parameter.Type is { } parameterType)
            {
                AnalyzeForbiddenTypeSyntax(context, helperGraph, parameterType);
            }
        }
    }

    private static void AnalyzeAnonymousFunctionAttributeTypeReferences(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
        => AnonymousFunctionAttributeAnalyzer.Analyze(context, helperGraph, AnalyzeForbiddenTypeSyntax);

    private static void AnalyzeForbiddenTypeSyntax(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        TypeSyntax typeSyntax)
    {
        var type = context.SemanticModel.GetTypeInfo(typeSyntax, context.CancellationToken).Type;
        AnalyzeForbiddenTypeSymbol(context, helperGraph, typeSyntax, type);
    }

    private static void AnalyzeForbiddenTypeSymbol(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        SyntaxNode locationNode,
        ITypeSymbol? type)
    {
        if (!TryGetForbiddenHostApi(type, out var forbidden))
        {
            return;
        }

        if (context.SemanticModel.GetEnclosingSymbol(
            context.Node.SpanStart,
            context.CancellationToken) is not IMethodSymbol method)
        {
            return;
        }

        helperGraph.RecordForbidden(method, forbidden);
        if (!IsEventKernel(method.ContainingType) ||
            !helperGraph.TryRecordDirectDiagnostic(method))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            locationNode.GetLocation(),
            forbidden.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }
}
