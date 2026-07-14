using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void RegisterEventAccessorAttributeMetadataAnalysis(
        CompilationStartAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
        => context.RegisterSyntaxNodeAction(
            c => AnalyzeEventAccessorAttributeMetadata(c, helperGraph),
            SyntaxKind.EventDeclaration);

    private static void AnalyzeEventAccessorAttributeMetadata(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        var declaration = (EventDeclarationSyntax)context.Node;
        foreach (var attributeList in declaration.AttributeLists)
        {
            if (attributeList.Target?.Identifier.ValueText != "method")
            {
                continue;
            }

            if (FirstForbiddenAttributeTypeOf(context, attributeList) is not { } forbiddenType)
            {
                continue;
            }

            if (context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is { } eventSymbol)
            {
                ReportAndRecordEventAccessorAttribute(context, helperGraph, eventSymbol, forbiddenType, attributeList);
            }

            return;
        }
    }

    private static ITypeSymbol? FirstForbiddenAttributeTypeOf(
        SyntaxNodeAnalysisContext context,
        AttributeListSyntax attributeList)
    {
        foreach (var typeOf in attributeList.DescendantNodes().OfType<TypeOfExpressionSyntax>())
        {
            var type = context.SemanticModel.GetTypeInfo(typeOf.Type, context.CancellationToken).Type;
            if (FirstForbiddenHostApi(type) is { } forbiddenType)
            {
                return forbiddenType;
            }
        }

        return null;
    }

    private static void ReportAndRecordEventAccessorAttribute(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IEventSymbol eventSymbol,
        ITypeSymbol forbiddenType,
        AttributeListSyntax attributeList)
    {
        if (eventSymbol.AddMethod is { } addMethod)
        {
            helperGraph.RecordForbidden(addMethod, forbiddenType);
        }

        if (eventSymbol.RemoveMethod is { } removeMethod)
        {
            helperGraph.RecordForbidden(removeMethod, forbiddenType);
        }

        if (!IsEventKernel(eventSymbol.ContainingType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            attributeList.GetLocation(),
            forbiddenType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }

    private static void AnalyzeEventReference(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var eventSymbol = ((IEventReferenceOperation)context.Operation).Event;
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            RecordInitializerEventRootCall(context, helperGraph, eventSymbol);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, eventSymbol.ContainingType);

        var (usesAdd, usesRemove) = EventAccessorUsage(context.Operation);
        var location = context.Operation.Syntax.GetLocation();
        if (usesAdd && eventSymbol.AddMethod is { } addMethod)
        {
            helperGraph.RecordCall(method, addMethod, location);
        }

        if (usesRemove && eventSymbol.RemoveMethod is { } removeMethod)
        {
            helperGraph.RecordCall(method, removeMethod, location);
        }
    }

    private static void RecordInitializerEventRootCall(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IEventSymbol eventSymbol)
    {
        if (context.ContainingSymbol is not (IFieldSymbol or IPropertySymbol))
        {
            return;
        }

        var containingType = context.ContainingSymbol.ContainingType;
        var (usesAdd, usesRemove) = EventAccessorUsage(context.Operation);
        var location = context.Operation.Syntax.GetLocation();
        if (usesAdd && eventSymbol.AddMethod is { } addMethod)
        {
            helperGraph.RecordInitializerRootCall(containingType, addMethod, location);
        }

        if (usesRemove && eventSymbol.RemoveMethod is { } removeMethod)
        {
            helperGraph.RecordInitializerRootCall(containingType, removeMethod, location);
        }
    }

    private static (bool Add, bool Remove) EventAccessorUsage(IOperation reference)
    {
        if (reference.Parent is IEventAssignmentOperation assignment &&
            ReferenceEquals(assignment.EventReference, reference))
        {
            return assignment.Adds ? (true, false) : (false, true);
        }

        return (true, true);
    }
}
