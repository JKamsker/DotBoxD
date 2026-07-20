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
            IIncrementOrDecrementOperation increment => increment.OperatorMethod,
            ICompoundAssignmentOperation compound => compound.OperatorMethod,
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
            RecordSynthesizedRecordEqualityInitializerRootCall(context, helperGraph, operatorMethod);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, operatorMethod.ContainingType);
        helperGraph.RecordCall(method, operatorMethod, context.Operation.Syntax.GetLocation());
        RecordSynthesizedRecordEqualityCall(context, helperGraph, method, operatorMethod);
        ReportLocalUseIfInvalid(context, operatorMethod);
    }

    private static void RecordSynthesizedRecordEqualityCall(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol caller,
        IMethodSymbol operatorMethod)
    {
        foreach (var target in operatorMethod.ContainingType.GetMembers("Equals"))
        {
            if (target is IMethodSymbol equalsMethod &&
                IsSourceRecordEqualityTarget(operatorMethod, equalsMethod))
            {
                helperGraph.RecordCall(caller, equalsMethod, context.Operation.Syntax.GetLocation());
            }
        }
    }

    private static void RecordSynthesizedRecordEqualityInitializerRootCall(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol operatorMethod)
    {
        foreach (var target in operatorMethod.ContainingType.GetMembers("Equals"))
        {
            if (target is IMethodSymbol equalsMethod &&
                IsSourceRecordEqualityTarget(operatorMethod, equalsMethod))
            {
                RecordInitializerRootCall(context, helperGraph, equalsMethod);
            }
        }
    }

    private static bool IsSourceRecordEqualityTarget(IMethodSymbol operatorMethod, IMethodSymbol equalsMethod)
        => operatorMethod is
        {
            MethodKind: MethodKind.UserDefinedOperator,
            IsImplicitlyDeclared: true,
            ContainingType.IsRecord: true,
            Parameters.Length: 2
        } &&
            operatorMethod.MetadataName is "op_Equality" or "op_Inequality" &&
            equalsMethod is
            {
                IsStatic: false,
                Name: "Equals",
                ReturnType.SpecialType: SpecialType.System_Boolean,
                Parameters.Length: 1
            } &&
            equalsMethod.DeclaringSyntaxReferences.Length != 0 &&
            SymbolEqualityComparer.Default.Equals(equalsMethod.Parameters[0].Type, operatorMethod.ContainingType);
    private static void AnalyzeImplicitStringFormatting(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        switch (context.Operation)
        {
            case IInterpolationOperation interpolation:
                RecordImplicitToStringCall(context, helperGraph, interpolation.Expression);
                break;
            case IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.Add,
                OperatorMethod: null
            } binary
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
