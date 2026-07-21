using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void RegisterAwaitReachabilityAnalysis(
        CompilationStartAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
        => context.RegisterSyntaxNodeAction(
            c => AnalyzeAwait(c, helperGraph),
            SyntaxKind.AwaitExpression);

    private static void RegisterAwaitUsingReachabilityAnalysis(
        CompilationStartAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
        => context.RegisterSyntaxNodeAction(
            c => AnalyzeAwaitUsing(c, helperGraph),
            SyntaxKind.LocalDeclarationStatement,
            SyntaxKind.UsingStatement);

    private static void AnalyzeAwait(SyntaxNodeAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is not AwaitExpressionSyntax awaitExpression ||
            context.ContainingSymbol is not IMethodSymbol method)
        {
            return;
        }

        var awaitInfo = context.SemanticModel.GetAwaitExpressionInfo(awaitExpression);
        var location = awaitExpression.GetLocation();
        RecordAwaiterCall(context, helperGraph, method, awaitInfo.GetAwaiterMethod, location);
        if (awaitInfo.IsCompletedProperty?.GetMethod is { } isCompletedGetter)
        {
            RecordAwaiterCall(context, helperGraph, method, isCompletedGetter, location);
        }

        RecordAwaiterCall(context, helperGraph, method, awaitInfo.GetResultMethod, location);
    }

    private static void RecordAwaiterCall(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        IMethodSymbol? awaiterMethod,
        Location location)
    {
        if (awaiterMethod is null)
        {
            return;
        }

        ReportAndRecordForbiddenAwaiter(context, helperGraph, method, awaiterMethod.ContainingType, location);
        helperGraph.RecordCall(method, awaiterMethod, location);
        ReportAndRecordForbiddenAwaiterResult(context, helperGraph, method, awaiterMethod, location);
        ReportLocalAwaiterUseIfInvalid(context, awaiterMethod, location);
    }

    private static void ReportAndRecordForbiddenAwaiter(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        ITypeSymbol? type,
        Location location)
    {
        if (!IsForbiddenHostApi(type))
        {
            return;
        }

        RecordAndReportForbiddenAwaiter(context, helperGraph, method, type!, location);
    }

    private static void ReportAndRecordForbiddenAwaiterResult(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol containingMethod,
        IMethodSymbol awaiterMethod,
        Location location)
    {
        if (!TryGetForbiddenHostApi(awaiterMethod.ReturnType, out var forbiddenType))
        {
            return;
        }

        RecordAndReportForbiddenAwaiter(context, helperGraph, containingMethod, forbiddenType, location);
    }

    private static void RecordAndReportForbiddenAwaiter(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol containingMethod,
        ITypeSymbol forbiddenType,
        Location location)
    {
        helperGraph.RecordForbidden(containingMethod, forbiddenType);
        if (!IsEventKernel(containingMethod.ContainingType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            location,
            forbiddenType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }

    private static void ReportLocalAwaiterUseIfInvalid(
        SyntaxNodeAnalysisContext context,
        ISymbol target,
        Location location)
    {
        if (!HasAttribute(target, DotBoxDMetadataNames.NativeOnlyAttribute) ||
            !IsLocalUseForbidden(context.Node, context.ContainingSymbol, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            PluginAnalyzerDiagnostics.LocalContextMemberRule,
            location,
            "[NativeOnly] context members run natively and cannot be used in lowered hook chains or server-extension bodies."));
    }

    private static void RecordAwaitablePatternCalls(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        ITypeSymbol? awaitableType,
        Location location)
    {
        if (awaitableType is null || awaitableType.DeclaringSyntaxReferences.Length == 0)
        {
            return;
        }

        foreach (var member in AwaiterPatternMethods(
            context.SemanticModel,
            awaitableType,
            location.SourceSpan.Start,
            context.CancellationToken))
        {
            RecordAwaiterCall(context, helperGraph, method, member, location);
            if (member.ReturnType.GetMembers("IsCompleted").OfType<IPropertySymbol>().FirstOrDefault()?.GetMethod is
                { } isCompletedGetter)
            {
                RecordAwaiterCall(context, helperGraph, method, isCompletedGetter, location);
            }

            var getResult = member.ReturnType
                .GetMembers("GetResult")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(static m => m.Parameters.Length == 0);
            RecordAwaiterCall(context, helperGraph, method, getResult, location);
            return;
        }
    }

    private static IEnumerable<IMethodSymbol> AwaiterPatternMethods(
        SemanticModel semanticModel,
        ITypeSymbol awaitableType,
        int position,
        CancellationToken cancellationToken)
    {
        foreach (var member in awaitableType.GetMembers("GetAwaiter").OfType<IMethodSymbol>())
        {
            if (!member.IsStatic && member.Parameters.Length == 0)
            {
                yield return member;
            }
        }

        var foundExtension = false;
        foreach (var symbol in semanticModel.LookupSymbols(
                position,
                name: "GetAwaiter",
                includeReducedExtensionMethods: true).OfType<IMethodSymbol>())
        {
            var method = symbol.ReducedFrom ?? symbol;
            if (IsAwaiterExtensionFor(semanticModel.Compilation, awaitableType, method))
            {
                foundExtension = true;
                yield return method;
            }
        }

        if (foundExtension)
        {
            yield break;
        }

        foreach (var method in SourceExtensionAwaiterPatternMethods(
            semanticModel,
            awaitableType,
            cancellationToken))
        {
            yield return method;
        }
    }

    private static IEnumerable<IMethodSymbol> SourceExtensionAwaiterPatternMethods(
        SemanticModel semanticModel,
        ITypeSymbol awaitableType,
        CancellationToken cancellationToken)
    {
        foreach (var declaration in semanticModel.SyntaxTree
            .GetRoot(cancellationToken)
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>())
        {
            if (!string.Equals(declaration.Identifier.ValueText, "GetAwaiter", StringComparison.Ordinal) ||
                semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not { } method ||
                !IsAwaiterExtensionFor(semanticModel.Compilation, awaitableType, method))
            {
                continue;
            }

            yield return method;
        }
    }

    private static bool IsAwaiterExtensionFor(
        Compilation compilation,
        ITypeSymbol awaitableType,
        IMethodSymbol method)
    {
        if (!method.IsExtensionMethod ||
            method.Parameters.Length == 0 ||
            method.Parameters[0].RefKind != RefKind.None ||
            method.ReturnsVoid)
        {
            return false;
        }

        var receiverConversion = compilation.ClassifyConversion(awaitableType, method.Parameters[0].Type);
        return receiverConversion.IsImplicit;
    }
}
