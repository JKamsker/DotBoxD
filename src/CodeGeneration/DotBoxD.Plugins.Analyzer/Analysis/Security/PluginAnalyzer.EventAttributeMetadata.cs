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
        var attributeLists = context.Node switch
        {
            EventDeclarationSyntax eventDeclaration when IsEventKernel(
                context.SemanticModel.GetDeclaredSymbol(eventDeclaration, context.CancellationToken)?.ContainingType)
                => eventDeclaration.AttributeLists,
            EventFieldDeclarationSyntax eventFieldDeclaration when IsEventFieldInEventKernel(context, eventFieldDeclaration)
                => eventFieldDeclaration.AttributeLists,
            _ => default
        };

        ReportForbiddenAttributeLists(context, attributeLists);
    }

    private static bool IsEventFieldInEventKernel(
        SyntaxNodeAnalysisContext context,
        EventFieldDeclarationSyntax eventFieldDeclaration)
        => eventFieldDeclaration.Declaration.Variables.Any(variable => IsEventKernel(
            context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken)?.ContainingType));

    private static void ReportForbiddenAttributeLists(
        SyntaxNodeAnalysisContext context,
        SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attribute in attributeLists.SelectMany(list => list.Attributes))
        {
            foreach (var argument in attribute.ArgumentList?.Arguments ?? [])
            {
                if (FirstForbiddenHostApi(context.SemanticModel.GetOperation(argument.Expression, context.CancellationToken)) is { } forbiddenType)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        ForbiddenHostApiRule,
                        argument.Expression.GetLocation(),
                        forbiddenType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                    return;
                }
            }
        }
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
}
