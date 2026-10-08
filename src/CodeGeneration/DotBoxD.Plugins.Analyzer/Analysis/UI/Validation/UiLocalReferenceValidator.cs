using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.UI;

/// <summary>Rejects CLR lifetime and mutable alias semantics that the value-based kernel IR cannot preserve.</summary>
internal static class UiLocalReferenceValidator
{
    public static void Validate(MethodDeclarationSyntax syntax, SemanticModel model, CancellationToken token)
    {
        var nodes = syntax.DescendantNodes().ToArray();
        if (nodes.OfType<LocalDeclarationStatementSyntax>().Any(local => !local.UsingKeyword.IsKind(SyntaxKind.None)))
        { throw new NotSupportedException("using declarations require CLR disposal and cannot execute as local kernels"); }

        var mutated = FindMutatedLocals(nodes, model, token);
        if (mutated.Count == 0)
        { return; }
        ValidateOwners(mutated, token);
        ValidateUses(nodes, mutated, model, token);
    }

    private static HashSet<ILocalSymbol> FindMutatedLocals(SyntaxNode[] nodes, SemanticModel model, CancellationToken token)
    {
        var mutated = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        foreach (var node in nodes)
        {
            token.ThrowIfCancellationRequested();
            var receiver = MutationReceiver(node, model, token);
            if (receiver is null)
            { continue; }
            if (receiver is not IdentifierNameSyntax identifier || model.GetSymbolInfo(identifier, token).Symbol is not ILocalSymbol local)
            { throw UnsupportedMutation(); }
            mutated.Add(local);
        }
        return mutated;
    }

    private static void ValidateOwners(HashSet<ILocalSymbol> mutated, CancellationToken token)
    {
        foreach (var local in mutated)
        {
            if (local.DeclaringSyntaxReferences.Single().GetSyntax(token) is not VariableDeclaratorSyntax declaration ||
                declaration.Initializer is not { } initializer || Unwrap(initializer.Value) is not BaseObjectCreationExpressionSyntax)
            { throw UnsupportedMutation(); }
        }
    }

    private static void ValidateUses(SyntaxNode[] nodes, HashSet<ILocalSymbol> mutated, SemanticModel model, CancellationToken token)
    {
        foreach (var identifier in nodes.OfType<IdentifierNameSyntax>())
        {
            token.ThrowIfCancellationRequested();
            if (model.GetSymbolInfo(identifier, token).Symbol is ILocalSymbol local && mutated.Contains(local) &&
                !IsDirectUse(identifier))
            { throw UnsupportedMutation(); }
        }
    }

    private static ExpressionSyntax? MutationReceiver(SyntaxNode node, SemanticModel model, CancellationToken token)
        => ListMutationReceiver(node, model, token) ?? MapMutationReceiver(node, model, token);

    private static ExpressionSyntax? ListMutationReceiver(SyntaxNode node, SemanticModel model, CancellationToken token)
    {
        if (node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } invocation &&
            model.GetSymbolInfo(invocation, token).Symbol is IMethodSymbol { Name: "Add", ReturnsVoid: true, IsStatic: false } method &&
            !method.IsExtensionMethod && method.ReducedFrom is null && method.Parameters.Length == 1 &&
            model.GetTypeInfo(member.Expression, token).Type is { } listType && DotBoxDRpcTypeMapper.ListElementType(listType) is not null)
        { return member.Expression; }
        return null;
    }

    private static ExpressionSyntax? MapMutationReceiver(SyntaxNode node, SemanticModel model, CancellationToken token)
    {
        if (node is AssignmentExpressionSyntax { Left: ElementAccessExpressionSyntax element } &&
            model.GetTypeInfo(element.Expression, token).Type is { } mapType && DotBoxDRpcTypeMapper.MapTypes(mapType) is not null)
        { return element.Expression; }
        return null;
    }

    private static bool IsDirectUse(IdentifierNameSyntax identifier)
    {
        ExpressionSyntax use = identifier;
        while (use.Parent is ParenthesizedExpressionSyntax parenthesized)
        { use = parenthesized; }
        return use.Parent switch
        {
            MemberAccessExpressionSyntax member => member.Expression == use,
            ElementAccessExpressionSyntax element => element.Expression == use,
            AssignmentExpressionSyntax assignment => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                assignment.Left == use && Unwrap(assignment.Right) is BaseObjectCreationExpressionSyntax,
            _ => false
        };
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        { expression = parenthesized.Expression; }
        return expression;
    }

    private static NotSupportedException UnsupportedMutation()
        => new("list/map mutation requires a newly created, unaliased local used only through direct collection operations; copying, passing or enumerating that mutable local is unsupported");
}
