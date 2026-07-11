using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcJsonExpressionStatementLowerer
{
    public static void LowerExpressionStatement(
        DotBoxDRpcJsonLowerer lowerer,
        ExpressionSyntax expression,
        List<string> output)
    {
        if (TryLowerAssignmentStatement(lowerer, expression, output) ||
            TryLowerIncrementStatement(lowerer, expression, output) ||
            TryLowerInvocationStatement(lowerer, expression, output))
        {
            return;
        }

        throw new NotSupportedException($"Server extension statement expression '{expression}' is not supported.");
    }

    private static bool TryLowerAssignmentStatement(
        DotBoxDRpcJsonLowerer lowerer,
        ExpressionSyntax expression,
        List<string> output)
    {
        if (expression is not AssignmentExpressionSyntax assignment)
        {
            return false;
        }

        if (assignment.Left is IdentifierNameSyntax target)
        {
            var value = assignment.Kind() == SyntaxKind.SimpleAssignmentExpression
                ? lowerer.LowerExpressionWithPrelude(assignment.Right, output)
                : LowerCompound(lowerer, assignment, target, output);
            output.Add(DotBoxDRpcJsonLowerer.SetStatement(target.Identifier.ValueText, value));
            return true;
        }

        if (assignment.Kind() == SyntaxKind.SimpleAssignmentExpression &&
            assignment.Left is ElementAccessExpressionSyntax element &&
            lowerer.TryLowerMapIndexSet(element, assignment.Right, output) is { } mapSet)
        {
            output.Add(mapSet);
            return true;
        }

        if (lowerer.AssignmentOverride?.Invoke(
                assignment,
                expression => lowerer.LowerExpressionWithPrelude(expression, output)) is { } lowered)
        {
            output.Add(lowered);
            return true;
        }

        return false;
    }

    private static bool TryLowerIncrementStatement(
        DotBoxDRpcJsonLowerer lowerer,
        ExpressionSyntax expression,
        List<string> output)
    {
        if (expression is PostfixUnaryExpressionSyntax { Operand: IdentifierNameSyntax postfixTarget } postfix &&
            postfix.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression)
        {
            output.Add(lowerer.IncrementStatement(postfixTarget, postfix.Kind()));
            return true;
        }

        if (expression is PrefixUnaryExpressionSyntax { Operand: IdentifierNameSyntax prefixTarget } prefix &&
            prefix.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression)
        {
            output.Add(lowerer.IncrementStatement(prefixTarget, prefix.Kind()));
            return true;
        }

        return false;
    }

    private static bool TryLowerInvocationStatement(
        DotBoxDRpcJsonLowerer lowerer,
        ExpressionSyntax expression,
        List<string> output)
    {
        if (expression is InvocationExpressionSyntax invocation)
        {
            output.Add(TryLowerListAdd(lowerer, invocation, output) ?? DotBoxDRpcJsonLowerer.SetStatement(
                lowerer.NextDiscardLocal(),
                lowerer.LowerExpressionWithPrelude(invocation, output)));
            return true;
        }

        if (expression is AwaitExpressionSyntax { Expression: InvocationExpressionSyntax awaitedInvocation })
        {
            output.Add(DotBoxDRpcJsonLowerer.SetStatement(
                lowerer.NextDiscardLocal(),
                lowerer.LowerExpressionWithPrelude(awaitedInvocation, output)));
            return true;
        }

        return false;
    }

    private static string LowerCompound(
        DotBoxDRpcJsonLowerer lowerer,
        AssignmentExpressionSyntax assignment,
        IdentifierNameSyntax target,
        List<string> output)
    {
        if (assignment.Kind() == SyntaxKind.AddAssignmentExpression &&
            lowerer.TypeOf(target).SpecialType == SpecialType.System_String)
        {
            return LowerStringConcatAssignment(lowerer, assignment, target, output);
        }

        var op = assignment.Kind() switch
        {
            SyntaxKind.AddAssignmentExpression => "add",
            SyntaxKind.SubtractAssignmentExpression => "sub",
            SyntaxKind.MultiplyAssignmentExpression => "mul",
            SyntaxKind.DivideAssignmentExpression => "div",
            SyntaxKind.ModuloAssignmentExpression => "rem",
            _ => throw new NotSupportedException($"Server extension assignment '{assignment.Kind()}' is not supported.")
        };
        return DotBoxDRpcJsonLowerer.BinaryJson(
            op,
            DotBoxDRpcJsonLowerer.Var(target.Identifier.ValueText),
            lowerer.LowerExpressionWithPrelude(assignment.Right, output));
    }

    private static string LowerStringConcatAssignment(
        DotBoxDRpcJsonLowerer lowerer,
        AssignmentExpressionSyntax assignment,
        IdentifierNameSyntax target,
        List<string> output)
    {
        if (!lowerer.IsStringExpression(assignment.Right))
        {
            throw new NotSupportedException(
                "Server extension operator '+=' requires both operands to be strings or matching numeric operands.");
        }

        lowerer.MarkAllocates();
        return DotBoxDRpcJsonLowerer.Call(
            "string.concatBudgeted",
            null,
            DotBoxDRpcJsonLowerer.Var(target.Identifier.ValueText),
            lowerer.LowerExpressionWithPrelude(assignment.Right, output));
    }

    private static string? TryLowerListAdd(
        DotBoxDRpcJsonLowerer lowerer,
        InvocationExpressionSyntax invocation,
        List<string> output)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Add" } member ||
            member.Expression is not IdentifierNameSyntax list ||
            invocation.ArgumentList.Arguments.Count != 1 ||
            DotBoxDRpcTypeMapper.ListElementType(lowerer.TypeOf(member.Expression)) is null)
        {
            return null;
        }

        var item = lowerer.LowerExpressionWithPrelude(invocation.ArgumentList.Arguments[0].Expression, output);
        var listName = list.Identifier.ValueText;
        lowerer.MarkAllocates();
        return DotBoxDRpcJsonLowerer.SetStatement(
            listName,
            DotBoxDRpcJsonLowerer.Call("list.add", null, DotBoxDRpcJsonLowerer.Var(listName), item));
    }
}
