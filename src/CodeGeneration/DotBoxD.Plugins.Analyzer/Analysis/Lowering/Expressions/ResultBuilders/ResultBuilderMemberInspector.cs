using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static class ResultBuilderMemberInspector
{
    public static bool UsesAuthorDefinedBuilderMember(
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol resultType)
    {
        var current = invocation;
        while (current.Expression is MemberAccessExpressionSyntax member)
        {
            if (HasAuthorDefinedMember(
                    resultType,
                    member.Name.Identifier.ValueText,
                    current.ArgumentList.Arguments.Count))
            {
                return true;
            }

            if (member.Expression is not InvocationExpressionSyntax inner)
            {
                return false;
            }

            current = inner;
        }

        return false;
    }

    public static bool HasHookResultAttribute(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(attribute.AttributeClass?.ToDisplayString(), DotBoxDMetadataNames.HookResultAttribute, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAuthorDefinedMember(INamedTypeSymbol resultType, string name, int parameterCount)
    {
        foreach (var member in resultType.GetMembers(name))
        {
            if (member.IsImplicitlyDeclared)
            {
                continue;
            }

            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } method)
            {
                if (method.Parameters.Length == parameterCount)
                {
                    return true;
                }

                continue;
            }

            if (member is IPropertySymbol or IFieldSymbol or IEventSymbol)
            {
                return true;
            }
        }

        return false;
    }
}
