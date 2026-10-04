using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;

namespace DotBoxD.Plugins.Analyzer.Analysis.MergeableIr;

internal static class MergeableIrStepGenerator
{
    private const string InterceptorContainerMetadataName =
        DotBoxDGenerationNames.TypeNames.GeneratedInterceptorsNamespace + ".MergeableIrStepInterceptors";

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

        var interceptorOutput = context.CompilationProvider
            .Combine(steps.Select(static (step, _) => step.Interception).Collect())
            .Select(static (pair, _) => new MergeableIrStepInterceptorOutput(
                pair.Right,
                pair.Left.GetTypeByMetadataName(InterceptorContainerMetadataName) is not null));

        GeneratorGuard.RegisterOutput(
            context,
            interceptorOutput,
            "mergeable IR step interceptor output",
            static (sourceContext, output) =>
            {
                if (output.HasContainerCollision)
                {
                    sourceContext.ReportDiagnostic(Diagnostic.Create(
                        PluginAnalyzerDiagnostics.UnsupportedKernelShapeRule,
                        Location.None,
                        "mergeable IR interceptor generation cannot use its reserved helper type " +
                        "'DotBoxD.Plugins.Generated.MergeableIrStepInterceptors'; rename the user-declared type."));
                    return;
                }

                MergeableIrStepInterceptorEmitter.Emit(sourceContext, output.Interceptions);
            });
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

internal sealed record MergeableIrStepInterceptorOutput(
    System.Collections.Immutable.ImmutableArray<MergeableIrStepInterception> Interceptions,
    bool HasContainerCollision);
