using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

/// <summary>
/// Rejects list/map mutation the value-based kernel IR cannot preserve. Kernel <c>list.add</c>/<c>map.set</c>
/// produce a new collection value that is stored back into the mutated local, so any other storage that
/// shares the CLR collection (an alias local, a DTO built from it, an argument) would silently keep the old
/// value. A mutated collection must therefore be a freshly created local (or, when allowed, a by-value
/// parameter) that is only ever used through direct collection operations.
/// </summary>
internal static class RpcLocalCollectionAliasValidator
{
    private const string UnsupportedMutationMessage =
        "list/map mutation requires a newly created, unaliased local used only through direct collection operations; copying, passing or enumerating that mutable local is unsupported";

    /// <param name="body">The kernel body (method or lambda) to validate.</param>
    /// <param name="model">The semantic model for <paramref name="body"/>.</param>
    /// <param name="token">The cancellation token.</param>
    /// <param name="valueBoundary">
    /// When true (server-extension and InvokeAsync bodies), by-value parameters may be mutated, and a mutated
    /// collection may be returned or enumerated by a <c>foreach</c> whose body does not mutate it: none of
    /// those observe a stale copy. When false (local UI kernels) the stricter documented UI rules apply.
    /// </param>
    public static void Validate(SyntaxNode body, SemanticModel model, CancellationToken token, bool valueBoundary)
    {
        var nodes = body.DescendantNodes().ToArray();
        var mutations = FindMutations(nodes, model, token, valueBoundary);
        if (mutations.Count == 0)
        {
            return;
        }

        ValidateOwners(mutations.Keys, token);
        ValidateUses(nodes, mutations, model, token, valueBoundary);
    }

    private static Dictionary<ISymbol, List<SyntaxNode>> FindMutations(
        SyntaxNode[] nodes,
        SemanticModel model,
        CancellationToken token,
        bool valueBoundary)
    {
        var mutations = new Dictionary<ISymbol, List<SyntaxNode>>(SymbolEqualityComparer.Default);
        foreach (var node in nodes)
        {
            token.ThrowIfCancellationRequested();
            var receiver = MutationReceiver(node, model, token);
            if (receiver is null)
            {
                continue;
            }

            if (Unwrap(receiver) is not IdentifierNameSyntax identifier ||
                model.GetSymbolInfo(identifier, token).Symbol is not { } symbol ||
                !IsOwnerKind(symbol, valueBoundary))
            {
                throw UnsupportedMutation();
            }

            if (!mutations.TryGetValue(symbol, out var sites))
            {
                sites = [];
                mutations.Add(symbol, sites);
            }

            sites.Add(node);
        }

        return mutations;
    }

    private static bool IsOwnerKind(ISymbol symbol, bool valueBoundary)
        => symbol is ILocalSymbol ||
           valueBoundary && symbol is IParameterSymbol { RefKind: RefKind.None };

    private static void ValidateOwners(IEnumerable<ISymbol> mutated, CancellationToken token)
    {
        foreach (var symbol in mutated)
        {
            if (symbol is IParameterSymbol)
            {
                continue;
            }

            if (symbol.DeclaringSyntaxReferences.Length != 1 ||
                symbol.DeclaringSyntaxReferences[0].GetSyntax(token) is not VariableDeclaratorSyntax declaration ||
                declaration.Initializer is not { } initializer || !IsFreshCollection(initializer.Value))
            {
                throw UnsupportedMutation();
            }
        }
    }

    private static void ValidateUses(
        SyntaxNode[] nodes,
        Dictionary<ISymbol, List<SyntaxNode>> mutations,
        SemanticModel model,
        CancellationToken token,
        bool valueBoundary)
    {
        foreach (var identifier in nodes.OfType<IdentifierNameSyntax>())
        {
            token.ThrowIfCancellationRequested();
            if (model.GetSymbolInfo(identifier, token).Symbol is { } symbol &&
                mutations.TryGetValue(symbol, out var sites) &&
                !IsDirectUse(identifier) &&
                !(valueBoundary && IsValueBoundaryUse(identifier, sites)))
            {
                throw UnsupportedMutation();
            }
        }
    }

    private static ExpressionSyntax? MutationReceiver(SyntaxNode node, SemanticModel model, CancellationToken token)
        => ListMutationReceiver(node, model, token) ?? MapMutationReceiver(node, model, token);

    private static ExpressionSyntax? ListMutationReceiver(SyntaxNode node, SemanticModel model, CancellationToken token)
    {
        if (node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } invocation &&
            model.GetSymbolInfo(invocation, token).Symbol is IMethodSymbol { Name: "Add", ReturnsVoid: true, IsStatic: false } method &&
            !method.IsExtensionMethod && method.ReducedFrom is null && method.Parameters.Length == 1 &&
            model.GetTypeInfo(member.Expression, token).Type is { } listType &&
            DotBoxDRpcTypeMapper.ListElementType(listType) is not null)
        {
            return member.Expression;
        }

        return null;
    }

    private static ExpressionSyntax? MapMutationReceiver(SyntaxNode node, SemanticModel model, CancellationToken token)
    {
        if (node is AssignmentExpressionSyntax { Left: ElementAccessExpressionSyntax element } &&
            model.GetTypeInfo(element.Expression, token).Type is { } mapType &&
            DotBoxDRpcTypeMapper.MapTypes(mapType) is not null)
        {
            return element.Expression;
        }

        return null;
    }

    private static bool IsDirectUse(IdentifierNameSyntax identifier)
    {
        var use = Outermost(identifier);
        return use.Parent switch
        {
            MemberAccessExpressionSyntax member => member.Expression == use,
            ElementAccessExpressionSyntax element => element.Expression == use,
            AssignmentExpressionSyntax assignment => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                assignment.Left == use && IsFreshCollection(assignment.Right),
            _ => false
        };
    }

    private static bool IsValueBoundaryUse(IdentifierNameSyntax identifier, List<SyntaxNode> mutationSites)
    {
        var use = Outermost(identifier);
        return use.Parent switch
        {
            // The returned snapshot is final: nothing can mutate the collection after the function returns.
            ReturnStatementSyntax => true,
            // Kernel foreach iterates a snapshot; CLR foreach throws if the body mutates the collection.
            ForEachStatementSyntax loop => loop.Expression == use &&
                !mutationSites.Exists(site => loop.Statement.Span.Contains(site.Span)),
            _ => false
        };
    }

    private static bool IsFreshCollection(ExpressionSyntax expression)
        => Unwrap(expression) is BaseObjectCreationExpressionSyntax or CollectionExpressionSyntax;

    private static ExpressionSyntax Outermost(ExpressionSyntax expression)
    {
        while (expression.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized;
        }

        return expression;
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static NotSupportedException UnsupportedMutation() => new(UnsupportedMutationMessage);
}
