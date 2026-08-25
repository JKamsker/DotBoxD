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
                      TryLowerDerivedListCount(expression, memberBindings, named, derived) ??
                      TryLowerDerivedUnary(expression, memberBindings, named, derived) ??
                      TryLowerDerivedBinary(expression, memberBindings, named, derived) ??
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
            LiteralExpressionSyntax literal =>
                LiteralJson(literal.Token.Value),
            IdentifierNameSyntax identifier => BoundDerivedMember(memberBindings, identifier),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } thisMember =>
                BoundDerivedMember(memberBindings, thisMember),
            _ => null
        };

    private string? TryLowerDerivedListCount(
        ExpressionSyntax expression,
        IReadOnlyDictionary<ISymbol, string> memberBindings,
        INamedTypeSymbol named,
        RecordMember derived)
    {
        if (expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Count" } count ||
            DotBoxDRpcTypeMapper.ListElementType(TypeOf(count.Expression)) is null)
        {
            return null;
        }

        return Call("list.count", null, LowerDerivedExpression(count.Expression, memberBindings, named, derived));
    }

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

    private static System.NotSupportedException DerivedNotSupported(INamedTypeSymbol named, RecordMember derived)
        => new(
            $"Server extension constructor for '{named.Name}' cannot build the derived member '{derived.Name}' in the " +
            "sandbox: its getter is not a simple expression over the constructor's parameters. Pass the value as a " +
            $"constructor parameter, or construct '{named.Name}' where the value is available.");
}
