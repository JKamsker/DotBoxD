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
}
