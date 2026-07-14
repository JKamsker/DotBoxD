using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed partial class InvokeAsyncCallShape
{
    private static bool TryGeneratedReceiverReturnType(
        InvocationExpressionSyntax invocation,
        BlockSyntax block,
        SemanticModel model,
        CancellationToken cancellationToken,
        int expectedTypeArgumentCount,
        int typeArgumentIndex,
        out ITypeSymbol returnType)
    {
        if (TryExplicitGenericTypeArgument(
                invocation,
                model,
                cancellationToken,
                expectedTypeArgumentCount,
                typeArgumentIndex,
                out returnType))
        {
            return true;
        }

        return InvokeAsyncLambdaShape.TryReturnType(block, model, cancellationToken, out returnType) ||
               HasUnresolvedReturnType(block, model, cancellationToken) &&
               TryContextReturnType(invocation, model, cancellationToken, out returnType);
    }

    private static bool HasUnresolvedReturnType(
        BlockSyntax block,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        foreach (var statement in block.DescendantNodes().OfType<ReturnStatementSyntax>())
        {
            if (statement.Expression is not null &&
                model.GetTypeInfo(statement.Expression, cancellationToken).Type is null or { TypeKind: TypeKind.Error })
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryContextReturnType(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        out ITypeSymbol returnType)
    {
        returnType = null!;
        if (!IsReturnedInvocation(invocation) ||
            model.GetEnclosingSymbol(invocation.SpanStart, cancellationToken) is not IMethodSymbol method ||
            DotBoxDRpcReturnType.PayloadType(method.ReturnType, model.Compilation) is not { } payload)
        {
            return false;
        }

        returnType = payload;
        return true;
    }

    private static bool IsReturnedInvocation(InvocationExpressionSyntax invocation)
    {
        SyntaxNode expression = invocation;
        while (true)
        {
            if (expression.Parent is ParenthesizedExpressionSyntax parenthesized &&
                parenthesized.Expression == expression)
            {
                expression = parenthesized;
                continue;
            }

            if (expression.Parent is AwaitExpressionSyntax awaitExpression &&
                awaitExpression.Expression == expression)
            {
                expression = awaitExpression;
                continue;
            }

            if (expression.Parent is ArrowExpressionClauseSyntax { Expression: var arrowValue })
            {
                return arrowValue == expression;
            }

            return expression.Parent is ReturnStatementSyntax { Expression: var returnValue } &&
                   returnValue == expression;
        }
    }

    private static bool TryExplicitGenericTypeArgument(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        int expectedTypeArgumentCount,
        int typeArgumentIndex,
        out ITypeSymbol type)
    {
        type = null!;
        if (GenericInvokeAsyncName(invocation.Expression) is not { } generic ||
            generic.TypeArgumentList.Arguments.Count != expectedTypeArgumentCount)
        {
            return false;
        }

        var candidate = model.GetTypeInfo(
            generic.TypeArgumentList.Arguments[typeArgumentIndex],
            cancellationToken).Type;
        if (candidate is null || candidate.TypeKind == TypeKind.Error)
        {
            return false;
        }

        type = candidate;
        return true;
    }

    private static GenericNameSyntax? GenericInvokeAsyncName(ExpressionSyntax expression)
        => expression switch
        {
            GenericNameSyntax { Identifier.ValueText: "InvokeAsync" } generic => generic,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.ValueText: "InvokeAsync" } generic } => generic,
            _ => null,
        };

    private static string BuildReturnTypeJson(
        ITypeSymbol returnType,
        IReadOnlyList<InvokeAsyncSyncOut> syncOuts,
        Compilation compilation)
    {
        if (syncOuts.Count == 0)
        {
            return DotBoxDRpcReturnType.JsonType(returnType, compilation);
        }

        var fields = new string[1 + syncOuts.Count];
        fields[0] = DotBoxDRpcReturnType.JsonType(returnType, compilation);
        for (var i = 0; i < syncOuts.Count; i++)
        {
            fields[i + 1] = DotBoxDRpcTypeMapper.JsonType(syncOuts[i].Type, compilation);
        }

        return "{\"name\":\"Record\",\"arguments\":[" + string.Join(",", fields) + "]}";
    }
}

internal sealed record InvokeAsyncCaptureParameter(string Name, INamedTypeSymbol Type);

internal sealed record InvokeAsyncSyncOut(
    string TargetName,
    ITypeSymbol Type,
    string LocalName,
    string? Initializer);
