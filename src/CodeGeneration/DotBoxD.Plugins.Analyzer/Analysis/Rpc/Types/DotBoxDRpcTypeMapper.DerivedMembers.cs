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
        while (TryMarkDerivedField(fields, reconstructable))
        {
        }

        return reconstructable.All(static item => item);
    }

    public static bool IsDerivedFromAssignedFields(
        RecordMember member,
        IReadOnlyList<RecordMember> fields,
        bool[] assigned,
        INamedTypeSymbol? dispatchType = null)
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

        var assignedFields = new Dictionary<string, ITypeSymbol>(StringComparer.Ordinal);
        for (var i = 0; i < fields.Count; i++)
        {
            if (assigned[i])
            {
                assignedFields.Add(fields[i].Name, fields[i].Type);
            }
        }

        return IsExpressionOverAssignedFields(body, assignedFields);
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

    private static bool TryMarkDerivedField(IReadOnlyList<RecordMember> fields, bool[] assigned)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (!assigned[i] && IsDerivedFromAssignedFields(fields[i], fields, assigned))
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
        IReadOnlyDictionary<string, ITypeSymbol> assignedFields)
        => new AssignedFieldExpressionVisitor(assignedFields).Visit(expression);

    private sealed class AssignedFieldExpressionVisitor(
        IReadOnlyDictionary<string, ITypeSymbol> assignedFields) : CSharpSyntaxVisitor<bool>
    {
        public override bool DefaultVisit(SyntaxNode node) => false;

        public override bool VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
            => Visit(node.Expression);

        public override bool VisitLiteralExpression(LiteralExpressionSyntax node) => true;

        public override bool VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
            => node.Contents.All(static content => content is InterpolatedStringTextSyntax);

        public override bool VisitIdentifierName(IdentifierNameSyntax node)
            => assignedFields.ContainsKey(node.Identifier.ValueText);

        public override bool VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (node.Expression is ThisExpressionSyntax or BaseExpressionSyntax)
            {
                return assignedFields.ContainsKey(node.Name.Identifier.ValueText);
            }

            return node.Name.Identifier.ValueText == "Count" &&
                   TryGetAssignedFieldType(node.Expression, assignedFields, out var type) &&
                   ListElementType(type) is not null &&
                   Visit(node.Expression);
        }

        public override bool VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
            => IsSupportedUnary(node) && Visit(node.Operand);

        public override bool VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
            => node.IsKind(SyntaxKind.SuppressNullableWarningExpression) && Visit(node.Operand);

        public override bool VisitCastExpression(CastExpressionSyntax node)
            => Visit(node.Expression);

        public override bool VisitBinaryExpression(BinaryExpressionSyntax node)
            => Visit(node.Left) && Visit(node.Right);

        public override bool VisitConditionalExpression(ConditionalExpressionSyntax node)
            => Visit(node.Condition) && Visit(node.WhenTrue) && Visit(node.WhenFalse);

        public override bool VisitInvocationExpression(InvocationExpressionSyntax node)
            => node.Expression is IdentifierNameSyntax { Identifier.Text: "nameof" };

        public override bool VisitSwitchExpression(SwitchExpressionSyntax node)
            => Visit(node.GoverningExpression) &&
               node.Arms.Count > 0 &&
               node.Arms[node.Arms.Count - 1].Pattern is DiscardPatternSyntax &&
               node.Arms.All(arm => arm.WhenClause is null &&
                                    IsSupportedSwitchPattern(arm.Pattern) &&
                                    Visit(arm.Expression));

        private static bool IsSupportedSwitchPattern(PatternSyntax pattern)
            => pattern is DiscardPatternSyntax or ConstantPatternSyntax or RelationalPatternSyntax;

        private static bool IsSupportedUnary(PrefixUnaryExpressionSyntax node)
            => node.IsKind(SyntaxKind.LogicalNotExpression) ||
               node.IsKind(SyntaxKind.UnaryMinusExpression) ||
               node.IsKind(SyntaxKind.UnaryPlusExpression);
    }

    private static bool TryGetAssignedFieldType(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, ITypeSymbol> assignedFields,
        out ITypeSymbol type)
    {
        type = null!;
        var name = GetAssignedFieldName(expression);

        return name is not null && assignedFields.TryGetValue(name, out type);
    }

    private static string? GetAssignedFieldName(ExpressionSyntax expression)
        => expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => GetAssignedFieldName(parenthesized.Expression),
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax or BaseExpressionSyntax } member =>
                member.Name.Identifier.ValueText,
            _ => null
        };

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
            TryGetHelperCall(invocation.Expression) is not { } helperCall ||
            TryGetParameterlessHelperExpression(
                containingType,
                helperCall.Name,
                helperCall.IsBaseQualified ? null : dispatchType,
                helperCall.IsBaseQualified) is not { } helper)
        {
            return expression;
        }

        return ExpandDerivedGetterExpression(helper.ContainingType, helper.Expression, dispatchType, depth + 1);
    }

    private static (string Name, bool IsBaseQualified)? TryGetHelperCall(ExpressionSyntax expression)
        => expression switch
        {
            IdentifierNameSyntax identifier => (identifier.Identifier.ValueText, false),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } member =>
                (member.Name.Identifier.ValueText, false),
            MemberAccessExpressionSyntax { Expression: BaseExpressionSyntax } member =>
                (member.Name.Identifier.ValueText, true),
            _ => null,
        };

    private static (INamedTypeSymbol ContainingType, ExpressionSyntax Expression)? TryGetParameterlessHelperExpression(
        INamedTypeSymbol containingType,
        string helperName,
        INamedTypeSymbol? dispatchType,
        bool isBaseQualified)
    {
        var startType = isBaseQualified ? containingType.BaseType : containingType;
        for (var currentType = startType; currentType is not null; currentType = currentType.BaseType)
        {
            foreach (var method in currentType.GetMembers(helperName).OfType<IMethodSymbol>())
            {
                if (!IsParameterlessInstanceHelper(method))
                {
                    continue;
                }

                var dispatchTarget = isBaseQualified ? method : ResolveDispatchTarget(method, dispatchType);
                if (TryGetMethodReturnExpression(dispatchTarget) is { } expression)
                {
                    return (currentType, expression);
                }
            }
        }

        return null;
    }

    private static ExpressionSyntax? TryGetMethodReturnExpression(IMethodSymbol method)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
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
