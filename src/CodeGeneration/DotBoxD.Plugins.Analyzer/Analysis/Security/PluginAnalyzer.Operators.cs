using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeOperator(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var operatorMethod = context.Operation switch
        {
            IUnaryOperation unary => unary.OperatorMethod,
            IBinaryOperation binary => binary.OperatorMethod,
            IConversionOperation conversion => conversion.OperatorMethod,
            _ => null
        };

        if (operatorMethod is null)
        {
            return;
        }

        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, operatorMethod.ContainingType);
            RecordInitializerRootCall(context, helperGraph, operatorMethod);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, operatorMethod.ContainingType);
        helperGraph.RecordCall(method, operatorMethod, context.Operation.Syntax.GetLocation());
        ReportLocalUseIfInvalid(context, operatorMethod);
    }

    private static void AnalyzeImplicitStringFormatting(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        switch (context.Operation)
        {
            case IInterpolationOperation interpolation:
                RecordImplicitToStringCall(context, helperGraph, interpolation.Expression);
                break;
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } binary
                when IsString(binary.Type):
                RecordImplicitToStringCall(context, helperGraph, binary.LeftOperand);
                RecordImplicitToStringCall(context, helperGraph, binary.RightOperand);
                break;
        }
    }

    private static void RecordImplicitToStringCall(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IOperation operation)
    {
        if (SourceToStringOverride(FormattedType(operation)) is not { } toString)
        {
            return;
        }

        if (context.ContainingSymbol is IMethodSymbol method)
        {
            helperGraph.RecordCall(method, toString, context.Operation.Syntax.GetLocation());
            return;
        }

        RecordInitializerRootCall(context, helperGraph, toString);
    }

    private static ITypeSymbol? FormattedType(IOperation operation)
        => operation is IConversionOperation { Type.SpecialType: SpecialType.System_Object } conversion
            ? conversion.Operand.Type
            : operation.Type;

    private static IMethodSymbol? SourceToStringOverride(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return null;
        }

        foreach (var member in named.GetMembers(nameof(ToString)).OfType<IMethodSymbol>())
        {
            if (member is
                {
                    IsOverride: true,
                    IsStatic: false,
                    Parameters.Length: 0,
                    ReturnType.SpecialType: SpecialType.System_String,
                    DeclaringSyntaxReferences.Length: > 0
                })
            {
                return member;
            }
        }

        return null;
    }

    private static bool IsString(ITypeSymbol? type)
        => type?.SpecialType == SpecialType.System_String;
}
