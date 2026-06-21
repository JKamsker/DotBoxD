using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static partial class DotBoxDConditionBodyModelFactory
{
    private static DotBoxDStatementBodyModel LowerAnd(
        BinaryExpressionSyntax binary,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context)
    {
        if (DotBoxDPatternExpressionLowerer.TryLowerDeclarationPattern(
                binary.Left,
                context,
                part => DotBoxDExpressionModelFactory.Create(part, context),
                out var left,
                out var captureName,
                out var capture))
        {
            if (DotBoxDPatternExpressionLowerer.ContainsDeclarationPattern(binary.Right))
            {
                throw new NotSupportedException($"Unsupported declaration-pattern composition '{binary}'.");
            }

            var rightContext = context.WithPatternCapture(captureName, capture);
            var right = LowerCondition(binary.Right, whenTrue, whenFalse, rightContext);
            return If(left.Source, right, whenFalse, left.Allocates);
        }

        if (DotBoxDPatternExpressionLowerer.ContainsDeclarationPattern(binary))
        {
            throw new NotSupportedException($"Unsupported declaration-pattern composition '{binary}'.");
        }

        var whenLeftTrue = LowerCondition(binary.Right, whenTrue, whenFalse, context);
        return LowerCondition(binary.Left, whenLeftTrue, whenFalse, context);
    }

    private static DotBoxDStatementBodyModel LowerOr(
        BinaryExpressionSyntax binary,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context)
    {
        if (DotBoxDPatternExpressionLowerer.ContainsDeclarationPattern(binary))
        {
            throw new NotSupportedException($"Unsupported declaration-pattern composition '{binary}'.");
        }

        var whenLeftFalse = LowerCondition(binary.Right, whenTrue, whenFalse, context);
        return LowerCondition(binary.Left, whenTrue, whenLeftFalse, context);
    }
}
