using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Detection;

public sealed class HookChainSyntaxCandidateTests
{
    [Theory]
    [InlineData("receiver.Run(Handle)", true)]
    [InlineData("receiver.RunLocal(Handle)", true)]
    [InlineData("receiver.Register(Handle)", true)]
    [InlineData("receiver.RegisterLocal(Handle)", true)]
    [InlineData("receiver.@Run(Handle)", true)]
    [InlineData("receiver.Run<int>(Handle)", true)]
    [InlineData("receiver.Where(Handle)", false)]
    [InlineData("receiver.Select(Handle)", false)]
    [InlineData("receiver.Touch()", false)]
    [InlineData("Run(Handle)", false)]
    [InlineData("receiver?.Run(Handle)", false)]
    public void Candidate_filter_selects_only_member_access_terminal_names(
        string expression,
        bool expected)
    {
        var tree = CSharpSyntaxTree.ParseText($$"""
            internal static class Candidate
            {
                private static void Test()
                {
                    {{expression}};
                }
            }
            """);
        var invocation = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First();

        Assert.Equal(expected, PluginPackageGenerator.IsHookChainTerminal(invocation));
    }
}
