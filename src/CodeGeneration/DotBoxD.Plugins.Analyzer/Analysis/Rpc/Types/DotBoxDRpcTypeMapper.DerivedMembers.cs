using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static partial class DotBoxDRpcTypeMapper
{
    public static bool CanReconstructWithObjectInitializer(
        INamedTypeSymbol type,
        IReadOnlyList<RecordMember> fields,
        Compilation? compilation = null)
    {
        if (fields.Count == 0 || (!type.IsValueType && !HasAccessibleParameterlessConstructor(type, compilation)))
        {
            return false;
        }

        return CanReconstructFromAssignedFields(fields, ObjectInitializerAssigned(fields, compilation), compilation);
    }

    public static bool CanReconstructFromAssignedFields(
        IReadOnlyList<RecordMember> fields,
        bool[] assigned,
        Compilation? compilation = null)
    {
        var reconstructable = ObjectInitializerAssigned(fields, assigned, compilation);
        while (TryMarkDerivedField(fields, reconstructable, compilation))
        {
        }

        return reconstructable.All(static item => item);
    }

    public static bool IsDerivedFromAssignedFields(
        RecordMember member,
        IReadOnlyList<RecordMember> fields,
        bool[] assigned,
        INamedTypeSymbol? dispatchType = null,
        Compilation? compilation = null)
    {
        if (member.Symbol is not IPropertySymbol
            {
                GetMethod: not null,
                SetMethod: null
            } property)
        {
            return false;
        }

        if (TryGetDerivedGetterExpression(property, dispatchType) is not { } body)
        {
            return false;
        }

        var assignedNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < fields.Count; i++)
        {
            if (assigned[i])
            {
                assignedNames.Add(fields[i].Name);
            }
        }

        return IsExpressionOverAssignedFields(body, assignedNames, compilation);
    }

    private static bool[] ObjectInitializerAssigned(
        IReadOnlyList<RecordMember> fields,
        Compilation? compilation = null)
    {
        var assigned = new bool[fields.Count];
        for (var i = 0; i < fields.Count; i++)
        {
            assigned[i] = IsObjectInitializerWritable(fields[i], compilation);
        }

        return assigned;
    }

    private static bool[] ObjectInitializerAssigned(
        IReadOnlyList<RecordMember> fields,
        bool[] alreadyAssigned,
        Compilation? compilation)
    {
        var assigned = new bool[fields.Count];
        for (var i = 0; i < fields.Count; i++)
        {
            assigned[i] = alreadyAssigned[i] || IsObjectInitializerWritable(fields[i], compilation);
        }

        return assigned;
    }

    private static bool TryMarkDerivedField(
        IReadOnlyList<RecordMember> fields,
        bool[] assigned,
        Compilation? compilation)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (!assigned[i] && IsDerivedFromAssignedFields(fields[i], fields, assigned, compilation: compilation))
            {
                assigned[i] = true;
                return true;
            }
        }

        return false;
    }

    private static bool HasAccessibleParameterlessConstructor(INamedTypeSymbol type, Compilation? compilation)
        => type.InstanceConstructors.Any(constructor =>
            constructor.Parameters.Length == 0 &&
            IsAccessibleFromGeneratedCode(constructor, compilation));

    private static bool IsExpressionOverAssignedFields(
        ExpressionSyntax expression,
        ISet<string> assignedNames,
        Compilation? compilation)
        => expression switch
        {
            ParenthesizedExpressionSyntax parenthesized =>
                IsExpressionOverAssignedFields(parenthesized.Expression, assignedNames, compilation),
            LiteralExpressionSyntax => true,
            IdentifierNameSyntax identifier =>
                assignedNames.Contains(identifier.Identifier.ValueText) || IsConstant(identifier, compilation),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } thisMember =>
                assignedNames.Contains(thisMember.Name.Identifier.ValueText) || IsConstant(thisMember, compilation),
            PrefixUnaryExpressionSyntax unary => IsSupportedUnary(unary) &&
                IsExpressionOverAssignedFields(unary.Operand, assignedNames, compilation),
            BinaryExpressionSyntax binary =>
                IsExpressionOverAssignedFields(binary.Left, assignedNames, compilation) &&
                IsExpressionOverAssignedFields(binary.Right, assignedNames, compilation),
            _ => false
        };

    private static bool IsConstant(ExpressionSyntax expression, Compilation? compilation)
        => compilation?.GetSemanticModel(expression.SyntaxTree).GetSymbolInfo(expression).Symbol is IFieldSymbol
        {
            IsConst: true,
            HasConstantValue: true
        };

    private static bool IsSupportedUnary(PrefixUnaryExpressionSyntax unary)
        => unary.IsKind(SyntaxKind.LogicalNotExpression) ||
           unary.IsKind(SyntaxKind.UnaryMinusExpression) ||
           unary.IsKind(SyntaxKind.UnaryPlusExpression);

    internal static ExpressionSyntax? TryGetDerivedGetterExpression(
        IPropertySymbol property,
        INamedTypeSymbol? dispatchType = null)
    {
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not PropertyDeclarationSyntax declaration)
            {
                continue;
            }

            if (declaration.ExpressionBody is { } arrow)
            {
                return ExpandDerivedGetterExpression(property.ContainingType, arrow.Expression, dispatchType);
            }

            var getter = declaration.AccessorList?.Accessors
                .FirstOrDefault(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
            if (getter?.ExpressionBody is { } getterArrow)
            {
                return ExpandDerivedGetterExpression(property.ContainingType, getterArrow.Expression, dispatchType);
            }

            if (getter?.Body is { Statements.Count: 1 } getterBody &&
                getterBody.Statements[0] is ReturnStatementSyntax { Expression: { } returned })
            {
                return ExpandDerivedGetterExpression(property.ContainingType, returned, dispatchType);
            }
        }

        return null;
    }

    private static ExpressionSyntax ExpandDerivedGetterExpression(
        INamedTypeSymbol containingType,
        ExpressionSyntax expression,
        INamedTypeSymbol? dispatchType,
        int depth = 0)
    {
        if (depth >= 4 ||
            expression is not InvocationExpressionSyntax
            {
                ArgumentList.Arguments.Count: 0
            } invocation ||
            HelperName(invocation.Expression) is not { } helperName ||
            TryGetParameterlessHelperExpression(containingType, helperName, dispatchType) is not { } helperBody)
        {
            return expression;
        }

        return ExpandDerivedGetterExpression(containingType, helperBody, dispatchType, depth + 1);
    }

    private static string? HelperName(ExpressionSyntax expression)
        => expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } member =>
                member.Name.Identifier.ValueText,
            _ => null,
        };

    private static ExpressionSyntax? TryGetParameterlessHelperExpression(
        INamedTypeSymbol containingType,
        string helperName,
        INamedTypeSymbol? dispatchType)
    {
        for (var currentType = containingType; currentType is not null; currentType = currentType.BaseType)
        {
            foreach (var method in currentType.GetMembers(helperName).OfType<IMethodSymbol>())
            {
                if (!IsParameterlessInstanceHelper(method))
                {
                    continue;
                }

                var dispatchTarget = ResolveDispatchTarget(method, dispatchType);
                foreach (var reference in dispatchTarget.DeclaringSyntaxReferences)
                {
                    if (reference.GetSyntax() is not MethodDeclarationSyntax declaration)
                    {
                        continue;
                    }

                    if (declaration.ExpressionBody is { } arrow)
                    {
                        return arrow.Expression;
                    }

                    if (declaration.Body is { Statements.Count: 1 } body &&
                        body.Statements[0] is ReturnStatementSyntax { Expression: { } returned })
                    {
                        return returned;
                    }
                }
            }
        }

        return null;
    }

    private static bool IsParameterlessInstanceHelper(IMethodSymbol method)
        => !method.IsStatic &&
           method.MethodKind == MethodKind.Ordinary &&
           method.Arity == 0 &&
           method.Parameters.Length == 0;

    private static IMethodSymbol ResolveDispatchTarget(IMethodSymbol method, INamedTypeSymbol? dispatchType)
    {
        if (!(method.IsVirtual || method.IsAbstract || method.IsOverride) || dispatchType is null)
        {
            return method;
        }

        for (var current = dispatchType; current is not null; current = current.BaseType)
        {
            foreach (var candidate in current.GetMembers(method.Name).OfType<IMethodSymbol>())
            {
                if (Overrides(candidate, method))
                {
                    return candidate;
                }
            }
        }

        return method;
    }

    private static bool Overrides(IMethodSymbol candidate, IMethodSymbol method)
    {
        for (var current = candidate.OverriddenMethod; current is not null; current = current.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(current, method))
            {
                return true;
            }
        }

        return false;
    }
}
