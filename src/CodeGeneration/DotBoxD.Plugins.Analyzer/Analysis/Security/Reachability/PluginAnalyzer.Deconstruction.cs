using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeDeconstruction(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Operation is not IDeconstructionAssignmentOperation ||
            context.Operation.Syntax is not AssignmentExpressionSyntax assignment ||
            context.Operation.SemanticModel.GetDeconstructionInfo(assignment).Method is not { } deconstruct)
        {
            return;
        }

        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, deconstruct.ContainingType);
            RecordForbiddenInitializerReference(context, helperGraph, deconstruct.ContainingType);
            RecordForbiddenDelegateInitializer(context, helperGraph, deconstruct.ContainingType);
            RecordStaticConstructorReachability(context, helperGraph, deconstruct);
            RecordForbiddenHelperPropertyInitializer(context, helperGraph, deconstruct.ContainingType);
            RecordInitializerRootCall(context, helperGraph, deconstruct);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, deconstruct.ContainingType);
        RecordStaticConstructorReachability(context, helperGraph, deconstruct);
        ReportForbiddenReferencedMethodSignature(context, deconstruct);
        helperGraph.RecordCall(method, deconstruct, context.Operation.Syntax.GetLocation());
        ReportLocalUseIfInvalid(context, deconstruct);
    }
}
