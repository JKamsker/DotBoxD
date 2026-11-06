using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcOperatorSemanticsValidator
{
    public static void Validate(BinaryExpressionSyntax binary, SemanticModel model, CancellationToken cancellationToken)
    {
        var left = model.GetTypeInfo(binary.Left, cancellationToken).Type;
        var right = model.GetTypeInfo(binary.Right, cancellationToken).Type;
        ValidateWireOperand(left);
        ValidateWireOperand(right);
        ValidateUnsignedEnumOrdering(binary.Kind(), left, right);
        ValidateEnumArithmetic(binary.Kind(), left, right);
        if (binary.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression)
        {
            ValidateReferenceOperand(left);
            ValidateReferenceOperand(right);
        }

        ValidateUserDefinedBinaryOperator(binary, model, cancellationToken);
    }

    /// <summary>
    /// The operator checks shared with the handle/hook-chain expression lowerer. Its operands are already
    /// restricted to scalar tags (and DateTime equality), so only the enum-width and user-defined operator
    /// rules apply: e.g. TimeSpan/TimeOnly ride as I64 ticks and DateOnly as an I32 day number, so their
    /// arithmetic operators (tick division, TimeOnly wrap-around) must not lower to plain integer math.
    /// </summary>
    public static void ValidateHandleBinary(BinaryExpressionSyntax binary, SemanticModel model, CancellationToken cancellationToken)
    {
        var left = model.GetTypeInfo(binary.Left, cancellationToken).Type;
        var right = model.GetTypeInfo(binary.Right, cancellationToken).Type;
        ValidateUnsignedEnumOrdering(binary.Kind(), left, right);
        ValidateEnumArithmetic(binary.Kind(), left, right);
        ValidateUserDefinedBinaryOperator(binary, model, cancellationToken);
    }

    /// <summary>Rejects relational patterns (<c>is &gt; X</c>) whose input cannot be ordered by its wire value.</summary>
    public static void ValidateRelationalPattern(ITypeSymbol? inputType)
        => ValidateUnsignedEnumOrdering(SyntaxKind.LessThanExpression, inputType, null);

    private static void ValidateUserDefinedBinaryOperator(
        BinaryExpressionSyntax binary,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(binary, cancellationToken).Symbol is
            IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } method &&
            !IsSupportedBinaryOperator(binary.Kind(), method.ContainingType))
        {
            throw new NotSupportedException(
                "This user-defined operator is unsupported; operate on supported scalar fields explicitly or use a remote handler.");
        }
    }

    public static void ValidateCompound(AssignmentExpressionSyntax assignment, SemanticModel model, CancellationToken cancellationToken)
    {
        ValidateWireOperand(model.GetTypeInfo(assignment.Left, cancellationToken).Type);
        ValidateWireOperand(model.GetTypeInfo(assignment.Right, cancellationToken).Type);
        ValidateEnumArithmetic(assignment.Kind(),
            model.GetTypeInfo(assignment.Left, cancellationToken).Type,
            model.GetTypeInfo(assignment.Right, cancellationToken).Type);
        if (model.GetSymbolInfo(assignment, cancellationToken).Symbol is
            IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } method &&
            !IsSupportedBinaryOperator(assignment.Kind(), method.ContainingType))
        {
            throw new NotSupportedException(
                "This user-defined compound operator is unsupported; operate on supported scalar fields explicitly or use a remote handler.");
        }
    }

    public static void ValidateUnary(PrefixUnaryExpressionSyntax unary, SemanticModel model, CancellationToken cancellationToken)
    {
        ValidateWireOperand(model.GetTypeInfo(unary.Operand, cancellationToken).Type);
        if (model.GetSymbolInfo(unary, cancellationToken).Symbol is
            IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } method &&
            method.ContainingType.SpecialType == SpecialType.None)
        {
            throw new NotSupportedException("This user-defined unary operator is unsupported; operate on supported scalar fields explicitly or use a remote handler.");
        }
    }

    private static void ValidateEnumArithmetic(SyntaxKind kind, ITypeSymbol? left, ITypeSymbol? right)
    {
        if (kind is SyntaxKind.AddExpression or SyntaxKind.SubtractExpression or
            SyntaxKind.AddAssignmentExpression or SyntaxKind.SubtractAssignmentExpression &&
            (RequiresEnumWidthConversion(left) || RequiresEnumWidthConversion(right)))
        {
            throw new NotSupportedException(
                "Arithmetic on enums backed by byte, sbyte, short, ushort or uint requires an underlying-width conversion that kernel arithmetic cannot preserve; use supported signed scalar arithmetic or a remote handler.");
        }
    }

    private static bool RequiresEnumWidthConversion(ITypeSymbol? type)
        => type is INamedTypeSymbol
        {
            TypeKind: TypeKind.Enum,
            EnumUnderlyingType:
            {
                SpecialType: SpecialType.System_Byte or SpecialType.System_SByte or
                SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_UInt32
            }
        };

    private static void ValidateUnsignedEnumOrdering(SyntaxKind kind, ITypeSymbol? left, ITypeSymbol? right)
    {
        if (IsRelational(kind) && (IsUnsigned64Enum(left) || IsUnsigned64Enum(right)))
        {
            throw new NotSupportedException(
                "Ordering ulong-backed enums is unsupported because their I64 wire representation has signed ordering; use a supported signed scalar or a remote handler.");
        }
    }

    private static bool IsUnsigned64Enum(ITypeSymbol? type)
        => type is INamedTypeSymbol
        {
            TypeKind: TypeKind.Enum,
            EnumUnderlyingType: { SpecialType: SpecialType.System_UInt64 }
        };

    private static void ValidateReferenceOperand(ITypeSymbol? type)
    {
        if (type is { IsReferenceType: true, SpecialType: not SpecialType.System_String })
        {
            throw new NotSupportedException(
                "Reference equality is unsupported because kernel collections and records have value semantics; compare supported scalar fields explicitly or use a remote handler.");
        }

    }

    private static void ValidateWireOperand(ITypeSymbol? type)
    {
        if (type is not null && DotBoxDRpcTypeMapper.IsFirstClassFrameworkWireStruct(type) &&
            !IsScalarFrameworkValue(type))
        {
            throw new NotSupportedException(
                $"Operators on '{type.ToDisplayString()}' are unsupported because its wire representation does not preserve CLR operator semantics; operate on supported scalar fields explicitly or use a remote handler.");
        }
    }

    private static bool IsSupportedBinaryOperator(SyntaxKind kind, ITypeSymbol type)
        => type.SpecialType is not (SpecialType.None or SpecialType.System_Decimal) ||
           IsComparison(kind) && IsScalarFrameworkValue(type);

    private static bool IsComparison(SyntaxKind kind)
        => kind is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression || IsRelational(kind);

    private static bool IsRelational(SyntaxKind kind)
        => kind is SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression
            or SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression;

    private static bool IsScalarFrameworkValue(ITypeSymbol type)
        => DotBoxDRpcTypeMapper.IsGuid(type) || DotBoxDRpcTypeMapper.IsDateOnlyWireType(type) ||
           DotBoxDRpcTypeMapper.IsTimeOnlyWireType(type) || DotBoxDRpcTypeMapper.IsTimeSpanWireType(type);
}
