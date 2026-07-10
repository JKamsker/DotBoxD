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
        CancellationToken cancellationToken)
    {
        SyntaxNode shouldHandle = FirstFilter(stages) ?? (SyntaxNode)invocation;
        SyntaxNode handle = HandleSource(stages, terminalLambda, isLocalCallback) ?? (SyntaxNode)invocation;
        return kernel with
        {
            ShouldHandleSource = KernelSourceLocationModel.Create(
                kernel.PluginId + ":ShouldHandle",
                shouldHandle,
                cancellationToken),
            HandleSource = KernelSourceLocationModel.Create(
                kernel.PluginId + ":Handle",
                handle,
                cancellationToken)
        };
    }

    public static PluginKernelModel ApplyToResult(
        PluginKernelModel kernel,
        InvocationExpressionSyntax invocation,
        IReadOnlyList<HookChainStage> stages,
        LambdaExpressionSyntax terminalLambda,
        bool isLocal,
        CancellationToken cancellationToken)
        => kernel with
        {
            ShouldHandleSource = KernelSourceLocationModel.Create(
                kernel.PluginId + ":ShouldHandle",
                FirstFilter(stages) ?? (SyntaxNode)invocation,
                cancellationToken),
            HandleSource = isLocal
                ? null
                : KernelSourceLocationModel.Create(
                    kernel.PluginId + ":Handle",
                    terminalLambda,
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
