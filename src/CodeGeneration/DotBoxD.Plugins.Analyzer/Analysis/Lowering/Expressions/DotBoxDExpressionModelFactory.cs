using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDExpressionModelFactory
{
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
        return expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => Lower(parenthesized.Expression, context),
            PrefixUnaryExpressionSyntax unary => LowerUnary(unary, context),
            BinaryExpressionSyntax binary => LowerBinary(binary, context),
            InvocationExpressionSyntax invocation =>
                DotBoxDInvocationExpressionLowerer.Lower(invocation, context, part => Lower(part, context)),
            IsPatternExpressionSyntax pattern => DotBoxDPatternExpressionLowerer.Lower(pattern, context, part => Lower(part, context)),
            IdentifierNameSyntax identifier when TryLowerImplicitThisIdentifier(identifier, context) is { } implicitThis =>
                implicitThis,
            IdentifierNameSyntax identifier => DotBoxDIdentifierExpressionLowerer.Lower(identifier, context),
            MemberAccessExpressionSyntax member
                when DotBoxDStringExpressionLowerer.TryLowerMember(member, context, part => Lower(part, context)) is { } lowered =>
                lowered,
            MemberAccessExpressionSyntax member => LowerMemberAccess(member, context),
            InterpolatedStringExpressionSyntax interpolated =>
                DotBoxDInterpolatedStringExpressionLowerer.Lower(interpolated, part => Lower(part, context)),
            BaseObjectCreationExpressionSyntax creation
                when DotBoxDRecordCreationExpressionLowerer.TryLower(creation, context, part => Lower(part, context)) is { } record =>
                record,
            AnonymousObjectCreationExpressionSyntax anonymous
                when DotBoxDAnonymousObjectCreationExpressionLowerer.TryLower(anonymous, context, part => Lower(part, context)) is { } anonymousRecord =>
                anonymousRecord,
            LiteralExpressionSyntax literal => DotBoxDLiteralExpressionLowerer.Lower(literal),
            _ => Unsupported(expression)
        };
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
                DotBoxDGenerationNames.Operators.LogicalNot,
                operand,
                DotBoxDGenerationNames.ManifestTypes.Bool,
                DotBoxDGenerationNames.ManifestTypes.Bool),
            SyntaxKind.UnaryMinusExpression => DotBoxDNumericExpressionLowerer.Unary(
                DotBoxDGenerationNames.Helpers.Neg,
                DotBoxDGenerationNames.Operators.Minus,
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
        return binary.Kind() switch
        {
            SyntaxKind.EqualsExpression => LowerEquality(negate: false),
            SyntaxKind.NotEqualsExpression => LowerEquality(negate: true),
            SyntaxKind.GreaterThanOrEqualExpression => LowerNumeric(DotBoxDGenerationNames.Helpers.Ge,
                DotBoxDGenerationNames.Operators.GreaterThanOrEqual, comparison: true),
            SyntaxKind.GreaterThanExpression => LowerNumeric(DotBoxDGenerationNames.Helpers.Gt,
                DotBoxDGenerationNames.Operators.GreaterThan, comparison: true),
            SyntaxKind.LessThanOrEqualExpression => LowerNumeric(DotBoxDGenerationNames.Helpers.Le,
                DotBoxDGenerationNames.Operators.LessThanOrEqual, comparison: true),
            SyntaxKind.LessThanExpression => LowerNumeric(DotBoxDGenerationNames.Helpers.Lt,
                DotBoxDGenerationNames.Operators.LessThan, comparison: true),
            SyntaxKind.LogicalAndExpression => LowerBool(
                DotBoxDGenerationNames.Helpers.And, DotBoxDGenerationNames.Operators.LogicalAnd),
            SyntaxKind.LogicalOrExpression => LowerBool(
                DotBoxDGenerationNames.Helpers.Or, DotBoxDGenerationNames.Operators.LogicalOr),
            SyntaxKind.AddExpression => AddBinary(left, right, allocates),
            SyntaxKind.SubtractExpression => LowerNumeric(DotBoxDGenerationNames.Helpers.Sub,
                DotBoxDGenerationNames.Operators.Minus, comparison: false),
            SyntaxKind.MultiplyExpression => LowerNumeric(DotBoxDGenerationNames.Helpers.Mul,
                DotBoxDGenerationNames.Operators.Multiply, comparison: false),
            SyntaxKind.DivideExpression => LowerNumeric(DotBoxDGenerationNames.Helpers.Div,
                DotBoxDGenerationNames.Operators.Divide, comparison: false),
            SyntaxKind.ModuloExpression => LowerNumeric(DotBoxDGenerationNames.Helpers.Mod,
                DotBoxDGenerationNames.Operators.Modulo, comparison: false),
            _ => Unsupported(binary)
        };

        DotBoxDExpressionModel LowerEquality(bool negate)
            => DotBoxDEqualityExpressionLowerer.Lower(
                left,
                right,
                negate,
                allocates,
                ConvertedType(binary.Left, context),
                ConvertedType(binary.Right, context));

        DotBoxDExpressionModel LowerNumeric(string helper, string symbol, bool comparison)
            => NumericBinary(helper, symbol, left, right, comparison, allocates);

        DotBoxDExpressionModel LowerBool(string helper, string symbol)
            => BoolBinary(helper, symbol, left, right, allocates);
    }
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
            DotBoxDGenerationNames.Operators.Add,
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
            // Projected record fields win over same-named event properties.
            if (context.ProjectedElementName is { } projectedName &&
                string.Equals(identifier.Identifier.ValueText, projectedName, StringComparison.Ordinal))
            {
                if (TryLowerProjectedRecordField(memberName, context) is { } projectedField)
                {
                    return projectedField;
                }
            }
            else if (string.Equals(identifier.Identifier.ValueText, context.EventParameterName, StringComparison.Ordinal))
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
