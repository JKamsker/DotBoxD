using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDResultBuilderExpressionLowerer
{
    // Walks the chain to its Ok()/Reject() seed and resolves the type the seed is called on. The seed receiver is
    // a type reference that exists in the pre-generation compilation even though Ok/Reject themselves do not.
    internal static INamedTypeSymbol? ResolveSeedResultType(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (!IsBuilderInvocation(invocation))
        {
            return null;
        }

        var current = invocation;
        while (true)
        {
            if (current.Expression is not MemberAccessExpressionSyntax member)
            {
                return null;
            }

            var name = member.Name.Identifier.ValueText;
            if (IsSeedName(name))
            {
                return ValidSeedResultType(invocation, member, semanticModel, cancellationToken);
            }

            if (TryGetWithReceiver(name, member, out var inner))
            {
                current = inner;
                continue;
            }

            return null;
        }
    }

    private static bool IsSeedName(string name)
        => string.Equals(name, OkMethod, StringComparison.Ordinal) ||
           string.Equals(name, RejectMethod, StringComparison.Ordinal);

    private static INamedTypeSymbol? ValidSeedResultType(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax member,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetSymbolInfo(member.Expression, cancellationToken).Symbol is not INamedTypeSymbol resultType)
        {
            return null;
        }

        if (!ResultBuilderMemberInspector.HasHookResultAttribute(resultType))
        {
            return null;
        }

        return ResultBuilderMemberInspector.UsesAuthorDefinedBuilderMember(invocation, resultType)
            ? null
            : resultType;
    }

    private static bool TryGetWithReceiver(
        string name,
        MemberAccessExpressionSyntax member,
        out InvocationExpressionSyntax inner)
    {
        inner = null!;
        if (!IsWithName(name))
        {
            return false;
        }

        if (member.Expression is not InvocationExpressionSyntax receiver)
        {
            return false;
        }

        inner = receiver;
        return true;
    }
}
