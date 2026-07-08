using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.MergeableIr;

internal static class MergeableIrStepGenerator
{
    public static void Register(IncrementalGeneratorInitializationContext context)
    {
        var results = GeneratorGuard.SyntaxValues(
            context,
            static (node, _) => IsCandidate(node),
            "mergeable IR step model",
            static (syntaxContext, ct) => MergeableIrStepModelFactory.Create(syntaxContext, ct));

        GeneratorGuard.RegisterOutput(
            context,
            results
                .Where(static result => result.Diagnostic is not null)
                .Select(static (result, _) => result.Diagnostic!),
            "mergeable IR step diagnostic output",
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));

        var steps = results
            .Where(static result => result.Step is not null)
            .Select(static (result, _) => result.Step!);

        GeneratorGuard.RegisterOutput(
            context,
            steps,
            "mergeable IR step source output",
            static (sourceContext, step) => MergeableIrStepSourceEmitter.Emit(sourceContext, step));

        GeneratorGuard.RegisterOutput(
            context,
            steps.Select(static (step, _) => step.Interception).Collect(),
            "mergeable IR step interceptor output",
            static (sourceContext, interceptions) => MergeableIrStepInterceptorEmitter.Emit(sourceContext, interceptions));
    }

    // Narrow syntactically before the semantic transform runs (mirrors IsHookChainTerminal): mergeable IR
    // targets are instance methods invoked on a receiver, and the source body must be a lambda argument.
    // Whether that lambda is marked through [LowerToIr] or paired with [IRBodyOf] is only knowable
    // semantically, and the lambda shape/arity checks stay in the factory so malformed marked calls still
    // surface a build-time diagnostic rather than being silently skipped here.
    private static bool IsCandidate(SyntaxNode node)
        => node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax,
            ArgumentList.Arguments.Count: > 0 and <= 2,
        } invocation &&
           invocation.ArgumentList.Arguments.Any(static argument => argument.Expression is LambdaExpressionSyntax);
}
