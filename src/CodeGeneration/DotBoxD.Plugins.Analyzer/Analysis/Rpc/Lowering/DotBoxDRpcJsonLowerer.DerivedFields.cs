using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    private static readonly IReadOnlyDictionary<SpecialType, object?> DefaultLiteralValues =
        new Dictionary<SpecialType, object?>
        {
            [SpecialType.System_Boolean] = false,
            [SpecialType.System_Byte] = 0,
            [SpecialType.System_Int16] = 0,
            [SpecialType.System_Int32] = 0,
            [SpecialType.System_UInt16] = 0,
            [SpecialType.System_UInt32] = 0L,
            [SpecialType.System_Int64] = 0L,
            [SpecialType.System_UInt64] = 0L,
            [SpecialType.System_Single] = 0D,
            [SpecialType.System_Double] = 0D,
            [SpecialType.System_String] = string.Empty,
        };

    private bool TryLowerDerivedField(
        IReadOnlyList<RecordMember> fields,
        bool[] assigned,
        string[] args,
        INamedTypeSymbol named)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (!assigned[i] && DotBoxDRpcTypeMapper.IsDerivedFromAssignedFields(fields[i], fields, assigned, named))
            {
                args[i] = LowerDerivedField(fields, assigned, args, named, fields[i]);
                assigned[i] = true;
                return true;
            }
        }

        return false;
    }

    // Builds the wire slot for a record field that has no constructor parameter — a derived/get-only member such
    // as `public int Sum => X + Y;`. The member is recomputed by the runtime on decode, but the sandbox record
    // value still needs a slot for it, and an in-sandbox read of the member reads that slot, so it must hold the
    // correct value. We lower the member's getter over the constructor-bound members (a name-based substitution:
    // each member reference becomes the already-lowered constructor argument for that member). Only a simple
    // expression over the constructor's members is supported — anything else, or a getter whose source is not
    // available (e.g. the record is declared in another assembly), is a clear diagnostic rather than a guess.
    private string LowerDerivedField(
        IReadOnlyList<RecordMember> fields,
        bool[] assigned,
        string[] args,
        INamedTypeSymbol named,
        RecordMember derived)
    {
        if (derived.Symbol is not IPropertySymbol { GetMethod: not null } property ||
            DotBoxDRpcTypeMapper.TryGetDerivedGetterExpression(property, named) is not { } body)
        {
            throw new System.NotSupportedException(
                $"Server extension constructor for '{named.Name}' cannot reconstruct the derived member '{derived.Name}' " +
                "(no inspectable getter is available — for example it is declared in another assembly). Construct " +
                $"'{named.Name}' where the value is available, or expose '{derived.Name}' as a constructor parameter.");
        }

        var memberBindings = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        for (var i = 0; i < fields.Count; i++)
        {
            if (assigned[i])
            {
                memberBindings[fields[i].Symbol] = args[i];
            }
        }

        AddIgnoredDefaultBindings(property.ContainingType, memberBindings);

        return ApplyNumericConversion(
            body,
            property.Type,
            LowerDerivedExpression(body, memberBindings, named, derived));
    }

    private string LowerDerivedExpression(
        ExpressionSyntax expression,
        IReadOnlyDictionary<ISymbol, string> memberBindings,
        INamedTypeSymbol named,
        RecordMember derived)
    {
        var lowered = TryLowerDerivedTerminal(expression, memberBindings, named, derived) ??
                      TryLowerDerivedUnary(expression, memberBindings, named, derived) ??
                      TryLowerDerivedCast(expression, memberBindings, named, derived) ??
                      TryLowerDerivedBinary(expression, memberBindings, named, derived) ??
                      TryLowerDerivedConditional(expression, memberBindings, named, derived) ??
                      TryLowerDerivedSwitch(expression, memberBindings, named, derived) ??
                      throw DerivedNotSupported(named, derived);

        return ApplyNumericConversion(expression, lowered);
    }

    private string? TryLowerDerivedTerminal(
        ExpressionSyntax expression,
        IReadOnlyDictionary<ISymbol, string> memberBindings,
        INamedTypeSymbol named,
        RecordMember derived)
        => expression switch
        {
            ParenthesizedExpressionSyntax parenthesized =>
                LowerDerivedExpression(parenthesized.Expression, memberBindings, named, derived),
            PostfixUnaryExpressionSyntax postfix
                when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) =>
                LowerDerivedExpression(postfix.Operand, memberBindings, named, derived),
            LiteralExpressionSyntax literal =>
                LiteralJson(literal.Token.Value),
            InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "nameof" } } nameofExpression when
                ModelFor(nameofExpression).GetConstantValue(nameofExpression, _cancellationToken) is { HasValue: true, Value: string name } =>
                LiteralJson(name),
            InterpolatedStringExpressionSyntax { Contents: var contents } when
                contents.All(static content => content is InterpolatedStringTextSyntax) =>
                LiteralJson(string.Concat(contents.Select(static content =>
                    ((InterpolatedStringTextSyntax)content).TextToken.ValueText))),
            IdentifierNameSyntax identifier => BoundDerivedMember(memberBindings, identifier),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } thisMember =>
                BoundDerivedMember(memberBindings, thisMember),
            _ => null
        };

    private string? BoundDerivedMember(
        IReadOnlyDictionary<ISymbol, string> memberBindings,
        ExpressionSyntax expression)
    {
        return ModelFor(expression).GetSymbolInfo(expression, _cancellationToken).Symbol is { } member &&
           memberBindings.TryGetValue(member, out var bound)
            ? bound
            : null;
    }

    private void AddIgnoredDefaultBindings(
        INamedTypeSymbol type,
        Dictionary<ISymbol, string> memberBindings)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol
                    {
                        DeclaredAccessibility: Accessibility.Public,
                        IsStatic: false,
                        GetMethod: { DeclaredAccessibility: Accessibility.Public }
                    } ignored ||
                    !DotBoxDRpcTypeMapper.IsIgnoredDataMember(ignored) ||
                    memberBindings.ContainsKey(ignored))
                {
                    continue;
                }

                if (TryDefaultLiteralJson(ignored.Type) is { } defaultValue)
                {
                    memberBindings.Add(ignored, defaultValue);
                }
            }
        }
    }

    private static string? TryDefaultLiteralJson(ITypeSymbol type)
        => DefaultLiteralValues.TryGetValue(type.SpecialType, out var value)
            ? LiteralJson(value)
            : null;

    private string? TryLowerDerivedUnary(
        ExpressionSyntax expression,
        IReadOnlyDictionary<ISymbol, string> memberBindings,
        INamedTypeSymbol named,
        RecordMember derived)
    {
        if (expression is not PrefixUnaryExpressionSyntax unary)
        {
            return null;
        }

        return unary.Kind() switch
        {
            SyntaxKind.LogicalNotExpression => Obj(
                ("unary", Str("not")),
                ("operand", LowerDerivedExpression(unary.Operand, memberBindings, named, derived))),
            SyntaxKind.UnaryMinusExpression => Obj(
                ("unary", Str("-")),
                ("operand", LowerDerivedExpression(unary.Operand, memberBindings, named, derived))),
            SyntaxKind.UnaryPlusExpression =>
                LowerDerivedExpression(unary.Operand, memberBindings, named, derived),
            _ => throw DerivedNotSupported(named, derived),
        };
    }

    private string? TryLowerDerivedBinary(
        ExpressionSyntax expression,
        IReadOnlyDictionary<ISymbol, string> memberBindings,
        INamedTypeSymbol named,
        RecordMember derived)
        => expression is BinaryExpressionSyntax binary
            ? LowerBinary(binary, part => LowerDerivedExpression(part, memberBindings, named, derived))
            : null;

    private string? TryLowerDerivedCast(
        ExpressionSyntax expression,
        IReadOnlyDictionary<ISymbol, string> memberBindings,
        INamedTypeSymbol named,
        RecordMember derived)
    {
        if (expression is not CastExpressionSyntax cast)
        {
            return null;
        }

        var targetType = ModelFor(cast).GetTypeInfo(cast, _cancellationToken).Type
                         ?? ModelFor(cast).GetTypeInfo(cast, _cancellationToken).ConvertedType
                         ?? throw DerivedNotSupported(named, derived);
        return ApplyRequiredConversion(
            cast.Expression,
            targetType,
            LowerDerivedExpression(cast.Expression, memberBindings, named, derived),
            $"Server extension derived member '{derived.Name}' cast '{cast}'");
    }

    private string? TryLowerDerivedConditional(
        ExpressionSyntax expression,
        IReadOnlyDictionary<ISymbol, string> memberBindings,
        INamedTypeSymbol named,
        RecordMember derived)
    {
        if (expression is not ConditionalExpressionSyntax conditional)
        {
            return null;
        }

        var localName = ReserveGeneratedLocal("__sir_derived");
        AddExpressionPrelude(Obj(
            ("op", Str("if")),
            ("condition", LowerDerivedExpression(conditional.Condition, memberBindings, named, derived)),
            ("then", "[" + SetStatement(localName, LowerDerivedExpression(conditional.WhenTrue, memberBindings, named, derived)) + "]"),
            ("else", "[" + SetStatement(localName, LowerDerivedExpression(conditional.WhenFalse, memberBindings, named, derived)) + "]")));
        return Var(localName);
    }

    private string? TryLowerDerivedSwitch(
        ExpressionSyntax expression,
        IReadOnlyDictionary<ISymbol, string> memberBindings,
        INamedTypeSymbol named,
        RecordMember derived)
    {
        if (expression is not SwitchExpressionSyntax { Arms.Count: > 0 } switchExpression ||
             switchExpression.Arms[switchExpression.Arms.Count - 1].Pattern is not DiscardPatternSyntax)
        {
            return null;
        }

        var localName = ReserveGeneratedLocal("__sir_derived");
        AddExpressionPrelude(SetStatement(
            localName,
            LowerDerivedExpression(switchExpression.Arms[switchExpression.Arms.Count - 1].Expression, memberBindings, named, derived)));

        var value = LowerDerivedExpression(switchExpression.GoverningExpression, memberBindings, named, derived);
        for (var i = switchExpression.Arms.Count - 2; i >= 0; i--)
        {
            var arm = switchExpression.Arms[i];
            if (arm.WhenClause is not null ||
                TryLowerDerivedSwitchCondition(arm.Pattern, value, memberBindings, named, derived) is not { } condition)
            {
                throw DerivedNotSupported(named, derived);
            }

            AddExpressionPrelude(Obj(
                ("op", Str("if")),
                ("condition", condition),
                ("then", "[" + SetStatement(
                    localName,
                    LowerDerivedExpression(arm.Expression, memberBindings, named, derived)) + "]"),
                ("else", "[]")));
        }

        return Var(localName);
    }

    private string? TryLowerDerivedSwitchCondition(
        PatternSyntax pattern,
        string value,
        IReadOnlyDictionary<ISymbol, string> memberBindings,
        INamedTypeSymbol named,
        RecordMember derived)
        => pattern switch
        {
            ConstantPatternSyntax constant => BinaryJson(
                "eq",
                value,
                LowerDerivedExpression(constant.Expression, memberBindings, named, derived)),
            RelationalPatternSyntax relational => BinaryJson(
                DerivedRelationalOperator(relational),
                value,
                LowerDerivedExpression(relational.Expression, memberBindings, named, derived)),
            _ => null
        };

    private static string DerivedRelationalOperator(RelationalPatternSyntax pattern)
        => pattern.OperatorToken.Kind() switch
        {
            SyntaxKind.GreaterThanToken => "gt",
            SyntaxKind.GreaterThanEqualsToken => "gte",
            SyntaxKind.LessThanToken => "lt",
            SyntaxKind.LessThanEqualsToken => "lte",
            _ => throw new NotSupportedException($"Unsupported switch pattern '{pattern}'.")
        };

    private static System.NotSupportedException DerivedNotSupported(INamedTypeSymbol named, RecordMember derived)
        => new(
            $"Server extension constructor for '{named.Name}' cannot build the derived member '{derived.Name}' in the " +
            "sandbox: its getter is not a simple expression over the constructor's parameters. Pass the value as a " +
            $"constructor parameter, or construct '{named.Name}' where the value is available.");
}
