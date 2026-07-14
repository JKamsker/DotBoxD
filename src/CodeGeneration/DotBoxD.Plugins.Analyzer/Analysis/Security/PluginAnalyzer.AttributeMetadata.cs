using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void RegisterAttributeMetadataAnalysis(AnalysisContext context)
        => context.RegisterSyntaxNodeAction(
            AnalyzeEventAttributeMetadata,
            SyntaxKind.EventDeclaration,
            SyntaxKind.EventFieldDeclaration);

    private static void AnalyzeEventAttributeMetadata(SyntaxNodeAnalysisContext context)
    {
        switch (context.Node)
        {
            case EventDeclarationSyntax eventDeclaration:
                AnalyzeEventDeclarationAttributeMetadata(context, eventDeclaration);
                break;
            case EventFieldDeclarationSyntax eventFieldDeclaration:
                AnalyzeEventFieldAttributeMetadata(context, eventFieldDeclaration);
                break;
        }
    }

    private static void AnalyzeEventDeclarationAttributeMetadata(
        SyntaxNodeAnalysisContext context,
        EventDeclarationSyntax eventDeclaration)
    {
        if (context.SemanticModel.GetDeclaredSymbol(
                eventDeclaration,
                context.CancellationToken) is IEventSymbol eventSymbol &&
            IsEventKernel(eventSymbol.ContainingType))
        {
            ReportForbiddenAttributeLists(context, eventDeclaration.AttributeLists);
        }
    }

    private static void AnalyzeEventFieldAttributeMetadata(
        SyntaxNodeAnalysisContext context,
        EventFieldDeclarationSyntax eventFieldDeclaration)
    {
        foreach (var variable in eventFieldDeclaration.Declaration.Variables)
        {
            if (context.SemanticModel.GetDeclaredSymbol(
                    variable,
                    context.CancellationToken) is IEventSymbol eventSymbol &&
                IsEventKernel(eventSymbol.ContainingType))
            {
                ReportForbiddenAttributeLists(context, eventFieldDeclaration.AttributeLists);
                return;
            }
        }
    }

    private static bool ReportForbiddenAttributeLists(
        SyntaxNodeAnalysisContext context,
        SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (ReportForbiddenAttributeSyntax(context, attribute))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ReportForbiddenAttributeSyntax(
        SyntaxNodeAnalysisContext context,
        AttributeSyntax attribute)
    {
        if (attribute.ArgumentList is null)
        {
            return false;
        }

        foreach (var argument in attribute.ArgumentList.Arguments)
        {
            var expression = argument.Expression;
            var operation = context.SemanticModel.GetOperation(expression, context.CancellationToken);
            if (FirstForbiddenHostApi(operation) is not { } forbiddenType)
            {
                continue;
            }

            ReportForbiddenType(context, expression.GetLocation(), forbiddenType);
            return true;
        }

        return false;
    }

    private static ITypeSymbol? FirstForbiddenHostApi(IOperation? operation)
    {
        if (operation is ITypeOfOperation typeOfOperation)
        {
            return FirstForbiddenHostApi(typeOfOperation.TypeOperand);
        }

        if (operation is null)
        {
            return null;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (FirstForbiddenHostApi(child) is { } forbiddenType)
            {
                return forbiddenType;
            }
        }

        return null;
    }

    private static void ReportForbiddenType(
        SyntaxNodeAnalysisContext context,
        Location? location,
        ITypeSymbol forbiddenType)
        => context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            location,
            forbiddenType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
}
