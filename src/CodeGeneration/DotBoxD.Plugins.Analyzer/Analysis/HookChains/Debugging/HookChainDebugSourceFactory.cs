using DotBoxD.Plugins.Analyzer.Analysis.Debugging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class HookChainDebugSourceFactory
{
    public static PluginKernelModel ApplyToSend(
        PluginKernelModel kernel,
        InvocationExpressionSyntax invocation,
        IReadOnlyList<HookChainStage> stages,
        LambdaExpressionSyntax terminalLambda,
        bool isLocalCallback,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var shouldHandle = FirstFilter(stages);
        SyntaxNode handle = HandleSource(stages, terminalLambda, isLocalCallback) ?? (SyntaxNode)invocation;
        return kernel with
        {
            ShouldHandleSource = shouldHandle is null
                ? null
                : KernelSourceLocationModel.CreateCompositeWithKernelMethods(
                    kernel.PluginId + ":ShouldHandle",
                    shouldHandle,
                    stages.Where(stage => !stage.IsSelect).Select(stage => (SyntaxNode)stage.Lambda),
                    semanticModel,
                    cancellationToken),
            HandleSource = KernelSourceLocationModel.CreateCompositeWithKernelMethods(
                kernel.PluginId + ":Handle",
                handle,
                stages.Select(stage => (SyntaxNode)stage.Lambda).Append(terminalLambda),
                semanticModel,
                cancellationToken)
        };
    }

    public static PluginKernelModel ApplyToResult(
        PluginKernelModel kernel,
        InvocationExpressionSyntax invocation,
        IReadOnlyList<HookChainStage> stages,
        LambdaExpressionSyntax terminalLambda,
        bool isLocal,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
        => kernel with
        {
            ShouldHandleSource = KernelSourceLocationModel.CreateCompositeWithKernelMethods(
                kernel.PluginId + ":ShouldHandle",
                FirstFilter(stages) ?? (SyntaxNode)invocation,
                stages.Where(stage => !stage.IsSelect).Select(stage => (SyntaxNode)stage.Lambda),
                semanticModel,
                cancellationToken),
            HandleSource = isLocal
                ? null
                : KernelSourceLocationModel.CreateCompositeWithKernelMethods(
                    kernel.PluginId + ":Handle",
                    terminalLambda,
                    stages.Select(stage => (SyntaxNode)stage.Lambda).Append(terminalLambda),
                    semanticModel,
                    cancellationToken)
        };

    private static LambdaExpressionSyntax? FirstFilter(IReadOnlyList<HookChainStage> stages)
    {
        foreach (var stage in stages)
        {
            if (!stage.IsSelect)
            {
                return stage.Lambda;
            }
        }

        return null;
    }

    private static LambdaExpressionSyntax? HandleSource(
        IReadOnlyList<HookChainStage> stages,
        LambdaExpressionSyntax terminalLambda,
        bool isLocalCallback)
    {
        if (!isLocalCallback)
        {
            return terminalLambda;
        }

        for (var index = stages.Count - 1; index >= 0; index--)
        {
            if (stages[index].IsSelect)
            {
                return stages[index].Lambda;
            }
        }

        return null;
    }
}
