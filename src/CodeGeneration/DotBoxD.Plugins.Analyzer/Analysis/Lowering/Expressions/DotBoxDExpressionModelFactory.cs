using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDExpressionModelFactory
{
    private static readonly Dictionary<SyntaxKind, BinaryLowerer> BinaryLowerers = new()
    {
        [SyntaxKind.EqualsExpression] = static (binary, context, left, right, allocates) =>
            LowerEqualityBinary(binary, context, left, right, negate: false, allocates),
        [SyntaxKind.NotEqualsExpression] = static (binary, context, left, right, allocates) =>
            LowerEqualityBinary(binary, context, left, right, negate: true, allocates),
        [SyntaxKind.GreaterThanOrEqualExpression] = static (_, _, left, right, allocates) =>
            NumericBinary(DotBoxDGenerationNames.Helpers.Ge, DotBoxDOperatorNames.GreaterThanOrEqual, left, right, comparison: true, allocates),
        [SyntaxKind.GreaterThanExpression] = static (_, _, left, right, allocates) =>
            NumericBinary(DotBoxDGenerationNames.Helpers.Gt, DotBoxDOperatorNames.GreaterThan, left, right, comparison: true, allocates),
        [SyntaxKind.LessThanOrEqualExpression] = static (_, _, left, right, allocates) =>
            NumericBinary(DotBoxDGenerationNames.Helpers.Le, DotBoxDOperatorNames.LessThanOrEqual, left, right, comparison: true, allocates),
        [SyntaxKind.LessThanExpression] = static (_, _, left, right, allocates) =>
            NumericBinary(DotBoxDGenerationNames.Helpers.Lt, DotBoxDOperatorNames.LessThan, left, right, comparison: true, allocates),
        [SyntaxKind.LogicalAndExpression] = static (_, _, left, right, allocates) =>
            BoolBinary(DotBoxDGenerationNames.Helpers.And, DotBoxDOperatorNames.LogicalAnd, left, right, allocates),
        [SyntaxKind.LogicalOrExpression] = static (_, _, left, right, allocates) =>
            BoolBinary(DotBoxDGenerationNames.Helpers.Or, DotBoxDOperatorNames.LogicalOr, left, right, allocates),
        [SyntaxKind.AddExpression] = static (_, _, left, right, allocates) => AddBinary(left, right, allocates),
        [SyntaxKind.SubtractExpression] = static (_, _, left, right, allocates) =>
            NumericBinary(DotBoxDGenerationNames.Helpers.Sub, DotBoxDOperatorNames.Minus, left, right, comparison: false, allocates),
        [SyntaxKind.MultiplyExpression] = static (_, _, left, right, allocates) =>
            NumericBinary(DotBoxDGenerationNames.Helpers.Mul, DotBoxDOperatorNames.Multiply, left, right, comparison: false, allocates),
        [SyntaxKind.DivideExpression] = static (_, _, left, right, allocates) =>
            NumericBinary(DotBoxDGenerationNames.Helpers.Div, DotBoxDOperatorNames.Divide, left, right, comparison: false, allocates),
        [SyntaxKind.ModuloExpression] = static (_, _, left, right, allocates) =>
            NumericBinary(DotBoxDGenerationNames.Helpers.Mod, DotBoxDOperatorNames.Modulo, left, right, comparison: false, allocates),
    };

    private delegate DotBoxDExpressionModel BinaryLowerer(
        BinaryExpressionSyntax binary,
        DotBoxDExpressionLoweringContext context,
        DotBoxDExpressionModel left,
        DotBoxDExpressionModel right,
        bool allocates);

    public static DotBoxDExpressionModel Create(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context)
        => Lower(expression, context);
    private static DotBoxDExpressionModel Lower(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (DotBoxDConstantExpressionLowerer.TryLower(
                expression,
                context.SemanticModel,
                context.CancellationToken) is { } constant)
        {
            return constant;
        }
        if (TryLowerBySyntax(expression, context, out var lowered))
        {
            return lowered;
        }

        return Unsupported(expression);
    }
    private static DotBoxDExpressionModel LowerUnary(
        PrefixUnaryExpressionSyntax unary,
        DotBoxDExpressionLoweringContext context)
    {
        if (DotBoxDLiteralExpressionLowerer.TryLowerNegative(unary) is { } literal)
        {
            return literal;
        }
        var operand = Lower(unary.Operand, context);
        return unary.Kind() switch
        {
            SyntaxKind.LogicalNotExpression => Unary(
                DotBoxDGenerationNames.Helpers.Not,
                DotBoxDOperatorNames.LogicalNot,
                operand,
                DotBoxDGenerationNames.ManifestTypes.Bool,
                DotBoxDGenerationNames.ManifestTypes.Bool),
            SyntaxKind.UnaryMinusExpression => DotBoxDNumericExpressionLowerer.Unary(
                DotBoxDGenerationNames.Helpers.Neg,
                DotBoxDOperatorNames.Minus,
                operand),
            _ => Unsupported(unary)
        };
    }
    private static DotBoxDExpressionModel Unary(
        string helper,
        string symbol,
        DotBoxDExpressionModel operand,
        string expected,
        string resultType)
    {
        RequireType(operand, expected, $"Unary operator '{symbol}'");
        return new DotBoxDExpressionModel($"{helper}({operand.Source})", resultType, operand.Allocates);
    }
    private static DotBoxDExpressionModel LowerBinary(
        BinaryExpressionSyntax binary,
        DotBoxDExpressionLoweringContext context)
    {
        if (DotBoxDPatternCaptureExpressionLowerer.TryLower(binary, context, Lower) is { } patternCapture)
        {
            return patternCapture;
        }
        var left = Lower(binary.Left, context);
        var right = Lower(binary.Right, context);
        DotBoxDNumericConstantPromoter.Promote(binary, context, ref left, ref right);
        var allocates = left.Allocates || right.Allocates;
        return BinaryLowerers.TryGetValue(binary.Kind(), out var lower)
            ? lower(binary, context, left, right, allocates)
            : Unsupported(binary);
    }

    private static DotBoxDExpressionModel LowerEqualityBinary(
        BinaryExpressionSyntax binary,
        DotBoxDExpressionLoweringContext context,
        DotBoxDExpressionModel left,
        DotBoxDExpressionModel right,
        bool negate,
        bool allocates)
        => DotBoxDEqualityExpressionLowerer.Lower(
            left,
            right,
            negate,
            allocates,
            ConvertedType(binary.Left, context),
            ConvertedType(binary.Right, context));
    private static DotBoxDExpressionModel AddBinary(
        DotBoxDExpressionModel left,
        DotBoxDExpressionModel right,
        bool allocates)
    {
        if (IsString(left) && IsString(right))
        {
            return new DotBoxDExpressionModel(
                $"{DotBoxDGenerationNames.Helpers.ConcatString}({left.Source}, {right.Source})",
                DotBoxDGenerationNames.ManifestTypes.String,
                true);
        }

        if (IsString(left) || IsString(right))
        {
            throw new NotSupportedException(
                "Operator '+' requires both operands to be strings or matching numeric operands.");
        }

        return NumericBinary(
            DotBoxDGenerationNames.Helpers.Add,
            DotBoxDOperatorNames.Add,
            left,
            right,
            comparison: false,
            allocates);
    }

    private static DotBoxDExpressionModel NumericBinary(
        string helper,
        string symbol,
        DotBoxDExpressionModel left,
        DotBoxDExpressionModel right,
        bool comparison,
        bool allocates)
        => DotBoxDNumericExpressionLowerer.Binary(helper, symbol, left, right, comparison, allocates);

    private static DotBoxDExpressionModel BoolBinary(
        string helper,
        string symbol,
        DotBoxDExpressionModel left,
        DotBoxDExpressionModel right,
        bool allocates)
    {
        RequireType(left, DotBoxDGenerationNames.ManifestTypes.Bool, $"Operator '{symbol}'");
        RequireType(right, DotBoxDGenerationNames.ManifestTypes.Bool, $"Operator '{symbol}'");
        return new DotBoxDExpressionModel(
            $"{helper}({left.Source}, {right.Source})",
            DotBoxDGenerationNames.ManifestTypes.Bool,
            allocates);
    }

    private static DotBoxDExpressionModel LowerMemberAccess(
        MemberAccessExpressionSyntax member,
        DotBoxDExpressionLoweringContext context)
    {
        var memberName = member.Name.Identifier.ValueText;
        if (member.Expression is IdentifierNameSyntax identifier)
        {
            if (TryLowerIdentifierMemberAccess(identifier, member, memberName, context) is { } identifierMember)
            {
                return identifierMember;
            }
        }

        if (member.Expression is ThisExpressionSyntax)
        {
            return LowerThisMemberAccess(member, memberName, context);
        }

        if (TryLowerContextMember(member, memberName, context) is { } contextMember)
        {
            return contextMember;
        }

        // General chains handle list counts/lengths and record fields on recursively lowered receivers.
        if (TryLowerMemberChain(member, memberName, context) is { } chained)
        {
            return chained;
        }

        return Unsupported(member);
    }

    private static DotBoxDExpressionModel? TryLowerIdentifierMemberAccess(
        IdentifierNameSyntax identifier,
        MemberAccessExpressionSyntax member,
        string memberName,
        DotBoxDExpressionLoweringContext context)
    {
        // Projected record fields win over same-named event properties.
        if (context.ProjectedElementName is { } projectedName &&
            string.Equals(identifier.Identifier.ValueText, projectedName, StringComparison.Ordinal))
        {
            return TryLowerProjectedRecordField(memberName, context);
        }

        if (!string.Equals(identifier.Identifier.ValueText, context.EventParameterName, StringComparison.Ordinal))
        {
            return null;
        }

        return LowerEventParameterMember(member, memberName, context);
    }

    private static DotBoxDExpressionModel LowerEventParameterMember(
        MemberAccessExpressionSyntax member,
        string memberName,
        DotBoxDExpressionLoweringContext context)
    {
        for (var i = 0; i < context.EventProperties.Count; i++)
        {
            var property = context.EventProperties[i];
            if (string.Equals(property.Name, memberName, StringComparison.Ordinal))
            {
                CollectEventPropertyCapability(member, context);
                return new DotBoxDExpressionModel(
                    $"{DotBoxDGenerationNames.Helpers.Var}({LiteralReader.StringLiteral(EventVariable(memberName))})",
                    property.Type,
                    false);
            }
        }

        throw new NotSupportedException($"Unknown event property '{memberName}'.");
    }

    public static string EventVariable(string name) => DotBoxDGenerationNames.GeneratedVariables.EventPrefix + name;

    private static void RequireType(DotBoxDExpressionModel expression, string expected, string context)
    {
        if (!string.Equals(expression.Type, expected, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"{context} requires {expected} operands.");
        }
    }

    internal static bool IsString(DotBoxDExpressionModel expression)
        => string.Equals(expression.Type, DotBoxDGenerationNames.ManifestTypes.String, StringComparison.Ordinal);

    private static ITypeSymbol? ConvertedType(ExpressionSyntax expression, DotBoxDExpressionLoweringContext context)
        => context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).ConvertedType;

    private static DotBoxDExpressionModel Unsupported(ExpressionSyntax expression)
        => throw new NotSupportedException($"Unsupported plugin expression '{expression}'.");
}
