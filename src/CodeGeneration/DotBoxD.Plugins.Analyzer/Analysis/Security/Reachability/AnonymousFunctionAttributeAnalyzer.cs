using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class AnonymousFunctionAttributeAnalyzer
{
    public static void Analyze(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        Action<SyntaxNodeAnalysisContext, ForbiddenHelperCallGraph, TypeSyntax> analyzeType)
    {
        if (context.Node is not LambdaExpressionSyntax lambda)
        {
            return;
        }

        AnalyzeAttributes(context, helperGraph, lambda.AttributeLists, analyzeType);
        if (lambda is ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters: var parameters })
        {
            foreach (var parameter in parameters)
            {
                AnalyzeAttributes(context, helperGraph, parameter.AttributeLists, analyzeType);
            }
        }
        else if (lambda is SimpleLambdaExpressionSyntax { Parameter: var parameter })
        {
            AnalyzeAttributes(context, helperGraph, parameter.AttributeLists, analyzeType);
        }
    }

    private static void AnalyzeAttributes(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        SyntaxList<AttributeListSyntax> attributes,
        Action<SyntaxNodeAnalysisContext, ForbiddenHelperCallGraph, TypeSyntax> analyzeType)
    {
        foreach (var typeOf in attributes.SelectMany(list => list.Attributes)
                     .SelectMany(attribute => attribute.DescendantNodes().OfType<TypeOfExpressionSyntax>()))
        {
            analyzeType(context, helperGraph, typeOf.Type);
        }
    }
}
