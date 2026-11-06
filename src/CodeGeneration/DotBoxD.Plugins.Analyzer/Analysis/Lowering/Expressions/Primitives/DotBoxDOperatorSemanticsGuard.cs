using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

/// <summary>
/// Rejects C# operator semantics the handle/hook-chain expression lowerer cannot reproduce on its wire tags,
/// mirroring the server-extension lowerer: user-defined operators (TimeSpan/TimeOnly/DateOnly ride as plain
/// integers), ulong-backed enum ordering, narrow-enum arithmetic, and System.Single rounding (float rides as
/// F64, so float arithmetic would silently run in double precision).
/// </summary>
internal static class DotBoxDOperatorSemanticsGuard
{
    public static void ValidateBinary(BinaryExpressionSyntax binary, DotBoxDExpressionLoweringContext context)
    {
        var model = context.SemanticModel;
        var token = context.CancellationToken;
        RpcOperatorSemanticsValidator.ValidateHandleBinary(binary, model, token);
        if (model.GetConstantValue(binary, token).HasValue)
        {
            return;
        }

        var description = $"Operator '{binary.OperatorToken.Text}'";
        if (model.GetTypeInfo(binary, token).Type?.SpecialType == SpecialType.System_Single)
        {
            throw UnsupportedSingleRounding(description);
        }

        ValidateSingleOperand(binary.Left, description, model, token);
        ValidateSingleOperand(binary.Right, description, model, token);
    }

    public static void ValidateUnary(PrefixUnaryExpressionSyntax unary, DotBoxDExpressionLoweringContext context)
        => RpcOperatorSemanticsValidator.ValidateUnary(unary, context.SemanticModel, context.CancellationToken);

    public static void ValidateRelationalPattern(RelationalPatternSyntax pattern, DotBoxDExpressionLoweringContext context)
        => RpcOperatorSemanticsValidator.ValidateRelationalPattern(
            context.SemanticModel.GetTypeInfo(pattern.Expression, context.CancellationToken).ConvertedType);

    private static void ValidateSingleOperand(
        ExpressionSyntax operand,
        string description,
        SemanticModel model,
        CancellationToken token)
    {
        var type = model.GetTypeInfo(operand, token);
        if (type.ConvertedType?.SpecialType == SpecialType.System_Single &&
            type.Type?.SpecialType != SpecialType.System_Single)
        {
            throw UnsupportedSingleRounding($"{description} operand '{operand}'");
        }
    }

    private static NotSupportedException UnsupportedSingleRounding(string description)
        => new($"{description} requires runtime System.Single rounding, which plugin F64 arithmetic cannot preserve; use double operands or a remote handler.");
}
