using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    private static Dictionary<string, int> ParameterOrdinals(IReadOnlyList<IParameterSymbol> parameters)
    {
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < parameters.Count; i++)
            ordinals.Add(parameters[i].Name, i);

        return ordinals;
    }

    private static IEnumerable<BoundKernelMethodArgument> ArgumentsInEvaluationOrder(
        InvocationExpressionSyntax invocation,
        BoundKernelMethodCall call)
    {
        var yielded = new HashSet<string>(StringComparer.Ordinal);
        if (invocation.Expression is MemberAccessExpressionSyntax member &&
            call.Arguments.Count > 0 &&
            call.Arguments[0].Expression is { } receiver &&
            SameSyntax(receiver, member.Expression))
        {
            yield return call.Arguments[0];
            yielded.Add(call.Arguments[0].Parameter.Name);
        }

        foreach (var syntaxArgument in invocation.ArgumentList.Arguments)
        {
            var bound = BoundArgumentForExpression(call, syntaxArgument.Expression)
                ?? throw new NotSupportedException(
                    $"[KernelMethod] '{call.Method.Name}' call argument could not be bound.");

            if (yielded.Add(bound.Parameter.Name))
            {
                yield return bound;
            }
        }

        foreach (var argument in call.Arguments)
        {
            if (yielded.Add(argument.Parameter.Name))
            {
                yield return argument;
            }
        }
    }

    private static BoundKernelMethodArgument? BoundArgumentForExpression(
        BoundKernelMethodCall call,
        ExpressionSyntax expression)
    {
        foreach (var argument in call.Arguments)
        {
            if (argument.Expression is { } candidate && SameSyntax(candidate, expression))
            {
                return argument;
            }
        }

        return null;
    }

    private static bool SameSyntax(ExpressionSyntax left, ExpressionSyntax right)
        => left.SyntaxTree == right.SyntaxTree && left.Span == right.Span;
}
