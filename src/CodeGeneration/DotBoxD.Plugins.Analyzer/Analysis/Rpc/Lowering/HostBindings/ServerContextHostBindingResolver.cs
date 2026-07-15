using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed class ServerContextHostBindingResolver
{
    private readonly SemanticModel _model;
    private readonly string? _contextParameterName;
    private readonly ITypeSymbol? _contextType;
    private readonly CancellationToken _cancellationToken;

    public ServerContextHostBindingResolver(
        SemanticModel model,
        string? contextParameterName,
        ITypeSymbol? contextType,
        CancellationToken cancellationToken)
    {
        _model = model;
        _contextParameterName = contextParameterName;
        _contextType = contextType;
        _cancellationToken = cancellationToken;
    }

    public ResolvedServerContextHostBinding? Resolve(
        InvocationExpressionSyntax invocation,
        IMethodSymbol? resolvedMethod)
    {
        if (resolvedMethod is not null ||
            invocation.Expression is not MemberAccessExpressionSyntax member ||
            !IsContextExpression(member.Expression))
        {
            return null;
        }

        var candidates = Candidates(member.Name.Identifier.ValueText, invocation.ArgumentList.Arguments);
        if (candidates.Count == 0)
        {
            return null;
        }

        if (UniqueBestCandidate(candidates) is { } resolved)
        {
            return new ResolvedServerContextHostBinding(resolved.Method, resolved.Binding);
        }

        throw new NotSupportedException(
            $"Server extension call '{invocation}' is ambiguous on server context type '{_contextType}'.");
    }

    public ITypeSymbol? TryGetInvocationReturnType(ExpressionSyntax expression)
    {
        if (expression is not InvocationExpressionSyntax invocation)
        {
            return null;
        }

        return Resolve(invocation, resolvedMethod: null) is { } resolved
            ? DotBoxDTypeNameReader.UnwrapTaskLike(resolved.Method.ReturnType)
            : null;
    }

    public ITypeSymbol? TryGetContextType(ExpressionSyntax expression)
        => IsContextExpression(expression) ? _contextType : null;

    private List<Candidate> Candidates(
        string methodName,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        var candidates = new List<Candidate>();
        foreach (var method in ContextMethods(methodName))
        {
            if (!DotBoxDRpcJsonLowerer.TryBindArgumentsInParameterOrder(
                    arguments,
                    method.Parameters,
                    description: null,
                    out var bound) ||
                !ArgumentsConvertImplicitly(method, bound))
            {
                continue;
            }

            if (DotBoxDHostBindingExpressionLowerer.HostBinding(method, _model.Compilation) is { } binding)
            {
                candidates.Add(new Candidate(method, binding, bound));
            }
        }

        return candidates;
    }

    private bool ArgumentsConvertImplicitly(
        IMethodSymbol method,
        DotBoxDRpcJsonLowerer.BoundRpcArguments bound)
    {
        foreach (var argument in bound.EvaluationOrder)
        {
            var targetType = method.Parameters[argument.ParameterIndex].Type;
            if (!_model.ClassifyConversion(argument.Expression, targetType).IsImplicit &&
                !argument.Expression.IsKind(SyntaxKind.DefaultLiteralExpression))
            {
                return false;
            }
        }

        return true;
    }

    private Candidate? UniqueBestCandidate(IReadOnlyList<Candidate> candidates)
    {
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        Candidate? best = null;
        foreach (var candidate in candidates)
        {
            if (!candidates.All(other => ReferenceEquals(candidate, other) || IsBetter(candidate, other)))
            {
                continue;
            }

            if (best is not null)
            {
                return null;
            }

            best = candidate;
        }

        return best;
    }

    private bool IsBetter(Candidate candidate, Candidate other)
    {
        var hasBetterConversion = false;
        for (var i = 0; i < candidate.Bound.EvaluationOrder.Count; i++)
        {
            var argument = candidate.Bound.EvaluationOrder[i].Expression;
            var candidateTarget = candidate.Method.Parameters[candidate.Bound.EvaluationOrder[i].ParameterIndex].Type;
            var otherTarget = other.Method.Parameters[other.Bound.EvaluationOrder[i].ParameterIndex].Type;
            switch (CompareConversion(argument, candidateTarget, otherTarget))
            {
                case BetterConversion.Other:
                    return false;
                case BetterConversion.Candidate:
                    hasBetterConversion = true;
                    break;
            }
        }

        return hasBetterConversion;
    }

    private BetterConversion CompareConversion(
        ExpressionSyntax argument,
        ITypeSymbol candidateTarget,
        ITypeSymbol otherTarget)
    {
        if (SymbolEqualityComparer.Default.Equals(candidateTarget, otherTarget))
        {
            return BetterConversion.Neither;
        }

        var argumentType = _model.GetTypeInfo(argument, _cancellationToken).Type;
        if (argumentType is null or { TypeKind: TypeKind.Error })
        {
            return BetterConversion.Neither;
        }

        if (SymbolEqualityComparer.Default.Equals(argumentType, candidateTarget))
        {
            return BetterConversion.Candidate;
        }

        if (SymbolEqualityComparer.Default.Equals(argumentType, otherTarget))
        {
            return BetterConversion.Other;
        }

        if (_model.Compilation is not CSharpCompilation compilation)
        {
            return BetterConversion.Neither;
        }

        var candidateToOther = compilation.ClassifyConversion(candidateTarget, otherTarget).IsImplicit;
        var otherToCandidate = compilation.ClassifyConversion(otherTarget, candidateTarget).IsImplicit;
        if (candidateToOther == otherToCandidate)
        {
            return BetterConversion.Neither;
        }

        return candidateToOther ? BetterConversion.Candidate : BetterConversion.Other;
    }

    private IEnumerable<IMethodSymbol> ContextMethods(string methodName)
    {
        if (_contextType is not INamedTypeSymbol named)
        {
            yield break;
        }

        for (INamedTypeSymbol? current = named; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                yield return method;
            }
        }

        foreach (var @interface in named.AllInterfaces)
        {
            foreach (var method in @interface.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                yield return method;
            }
        }
    }

    private bool IsContextExpression(ExpressionSyntax expression)
        => expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => IsContextExpression(parenthesized.Expression),
            ThisExpressionSyntax => _contextType is not null && string.IsNullOrEmpty(_contextParameterName),
            IdentifierNameSyntax identifier => string.Equals(
                identifier.Identifier.ValueText,
                _contextParameterName,
                StringComparison.Ordinal),
            _ => false
        };

    private enum BetterConversion
    {
        Neither,
        Candidate,
        Other
    }

    private sealed record Candidate(
        IMethodSymbol Method,
        (string BindingId, string? Capability, IReadOnlyList<string> Effects, bool IsAsync) Binding,
        DotBoxDRpcJsonLowerer.BoundRpcArguments Bound);
}

internal readonly record struct ResolvedServerContextHostBinding(
    IMethodSymbol Method,
    (string BindingId, string? Capability, IReadOnlyList<string> Effects, bool IsAsync) Binding);
