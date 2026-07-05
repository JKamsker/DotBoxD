using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDKernelMethodInliner
{
    private static DescriptorRequirements RecomputeDescriptorRequirements(
        IMethodSymbol method,
        DotBoxDExpressionLoweringContext context,
        KernelMethodDescriptorPayload descriptor)
    {
        var capabilities = new SortedSet<string>(StringComparer.Ordinal);
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var bindingId in DescriptorCallBindingIds(descriptor.Source))
        {
            if (IntrinsicCall(bindingId))
            {
                if (string.Equals(bindingId, "record.new", StringComparison.Ordinal))
                {
                    effects.Add(DotBoxDGenerationNames.Effects.Alloc);
                }

                continue;
            }

            if (HostBindingRequirements(method.ContainingType, context.SemanticModel.Compilation, bindingId)
                is not { } requirements)
            {
                throw new NotSupportedException(
                    $"Generated descriptor for context [KernelMethod] '{method.Name}' contains an untrusted host call '{bindingId}'.");
            }

            AddBindingRequirements(capabilities, effects, requirements);
        }

        if (!SameSet(capabilities, descriptor.Capabilities) ||
            !SameSet(effects, descriptor.Effects))
        {
            throw new NotSupportedException(
                $"Generated descriptor for context [KernelMethod] '{method.Name}' does not match recomputed host requirements.");
        }

        return new DescriptorRequirements(capabilities.ToArray(), effects.ToArray());
    }

    private static IEnumerable<string> DescriptorCallBindingIds(string source)
    {
        var expression = SyntaxFactory.ParseExpression(source);
        if (expression.ContainsDiagnostics)
        {
            throw new NotSupportedException("Generated descriptor contains invalid expression source.");
        }

        ValidateDescriptorSourceShape(expression);
        foreach (var creation in expression.DescendantNodesAndSelf().OfType<ObjectCreationExpressionSyntax>())
        {
            if (!IsCallExpression(creation.Type))
            {
                continue;
            }

            var argument = creation.ArgumentList?.Arguments.FirstOrDefault();
            if (argument?.Expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression) &&
                literal.Token.ValueText is { Length: > 0 } bindingId)
            {
                yield return bindingId;
                continue;
            }

            throw new NotSupportedException("Generated descriptor contains a non-literal host call binding id.");
        }
    }

    private static void ValidateDescriptorSourceShape(ExpressionSyntax expression)
    {
        ValidateDescriptorInvocations(expression);
        ValidateDescriptorCreations(expression);
        ValidateDescriptorArrays(expression);
        ValidateDescriptorForbiddenSyntax(expression);
        ValidateDescriptorMemberAccess(expression);
    }

    private static void ValidateDescriptorInvocations(ExpressionSyntax expression)
    {
        foreach (var invocation in expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (!IsAllowedInvocation(invocation.Expression))
            {
                throw new NotSupportedException("Generated descriptor contains unsupported expression source.");
            }
        }
    }

    private static void ValidateDescriptorCreations(ExpressionSyntax expression)
    {
        foreach (var creation in expression.DescendantNodesAndSelf().OfType<ObjectCreationExpressionSyntax>())
        {
            if (!IsCallExpression(creation.Type) ||
                creation.Initializer is not null)
            {
                throw new NotSupportedException("Generated descriptor contains unsupported expression source.");
            }
        }

        foreach (var creation in expression.DescendantNodesAndSelf().OfType<ImplicitObjectCreationExpressionSyntax>())
        {
            throw new NotSupportedException("Generated descriptor contains unsupported expression source.");
        }
    }

    private static void ValidateDescriptorArrays(ExpressionSyntax expression)
    {
        foreach (var array in expression.DescendantNodesAndSelf().OfType<ArrayCreationExpressionSyntax>())
        {
            if (!IsSandboxTypeArray(array.Type))
            {
                throw new NotSupportedException("Generated descriptor contains unsupported expression source.");
            }
        }

        foreach (var array in expression.DescendantNodesAndSelf().OfType<ImplicitArrayCreationExpressionSyntax>())
        {
            throw new NotSupportedException("Generated descriptor contains unsupported expression source.");
        }
    }

    private static void ValidateDescriptorForbiddenSyntax(ExpressionSyntax expression)
    {
        foreach (var spread in expression.DescendantNodesAndSelf().OfType<SpreadElementSyntax>())
        {
            throw new NotSupportedException("Generated descriptor contains unsupported expression source.");
        }

        foreach (var cast in expression.DescendantNodesAndSelf().OfType<CastExpressionSyntax>())
        {
            throw new NotSupportedException("Generated descriptor contains unsupported expression source.");
        }
    }

    private static void ValidateDescriptorMemberAccess(ExpressionSyntax expression)
    {
        foreach (var member in expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            if (!IsAllowedMemberAccess(member))
            {
                throw new NotSupportedException("Generated descriptor contains unsupported expression source.");
            }
        }
    }

    private static bool IsAllowedInvocation(ExpressionSyntax expression)
        => expression switch
        {
            IdentifierNameSyntax identifier => AllowedHelper(identifier.Identifier.ValueText),
            MemberAccessExpressionSyntax access => IsSandboxTypeMember(access) &&
                access.Name.Identifier.ValueText is "List" or "Map" or "Record",
            _ => false
        };

    private static bool IsAllowedMemberAccess(MemberAccessExpressionSyntax access)
        => IsSandboxTypeMember(access) ||
           DotBoxDGenerationNames.TypeNames.GlobalSandboxType.StartsWith(access.ToString(), StringComparison.Ordinal);

    private static bool IsSandboxTypeMember(MemberAccessExpressionSyntax access)
    {
        var receiver = access.Expression.ToString();
        return string.Equals(receiver, DotBoxDGenerationNames.TypeNames.GlobalSandboxType, StringComparison.Ordinal) &&
            access.Name.Identifier.ValueText is "Bool" or "I32" or "I64" or "F64" or "String" or "Guid" or
                "List" or "Map" or "Record";
    }

    private static bool IsSandboxTypeArray(ArrayTypeSyntax type)
        => type.RankSpecifiers.Count == 1 &&
           type.RankSpecifiers[0].Sizes.Count == 1 &&
           string.Equals(type.ElementType.ToString(), DotBoxDGenerationNames.TypeNames.GlobalSandboxType, StringComparison.Ordinal);

    private static bool AllowedHelper(string name)
        => string.Equals(name, "Var", StringComparison.Ordinal) ||
           PrimitiveLiteralHelpers.Contains(name) ||
           InvocationShapeResolvers.ContainsKey(name);

    private static bool IsCallExpression(TypeSyntax type)
    {
        var text = type.ToString();
        return string.Equals(text, DotBoxDGenerationNames.TypeNames.GlobalCallExpression, StringComparison.Ordinal);
    }

    private static bool IntrinsicCall(string bindingId)
        => bindingId is "record.new" or "record.get" or "list.count";

    private static bool SameSet(SortedSet<string> recomputed, EquatableArray<string> declared)
        => recomputed.SetEquals(declared);

    private sealed record DescriptorRequirements(IReadOnlyList<string> Capabilities, IReadOnlyList<string> Effects);
}
