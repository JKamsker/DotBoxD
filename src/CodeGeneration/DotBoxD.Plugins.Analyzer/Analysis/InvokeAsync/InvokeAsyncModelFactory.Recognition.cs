using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static partial class InvokeAsyncModelFactory
{
    private static bool IsUnqualifiedInvocationExpression(ExpressionSyntax expression)
        => expression is IdentifierNameSyntax or GenericNameSyntax;

    private static bool IsConditionalInvocationExpression(ExpressionSyntax expression)
        => expression is MemberBindingExpressionSyntax;
}
