namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiNonGenericStackConstructionReachabilityTests
{
    [Theory]
    [InlineData(
        "Stack capacity",
        "_ = new System.Collections.Stack(int.MaxValue);",
        true)]
    [InlineData(
        "Stack collection copy",
        "_ = new System.Collections.Stack(e);",
        true)]
    [InlineData(
        "parameterless Stack control",
        "_ = new System.Collections.Stack();",
        false)]
    public async Task Reports_unbounded_non_generic_stack_construction(
        string testCase,
        string operation,
        bool expectsForbiddenApi)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(operation),
            "DotBoxDPluginAnalyzerNonGenericStackConstructionReachabilityTest");

        var forbiddenApiDiagnostics = diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001");

        if (!expectsForbiddenApi)
        {
            Assert.Empty(forbiddenApiDiagnostics);
            return;
        }

        var diagnostic = Assert.Single(forbiddenApiDiagnostics);
        var message = diagnostic.GetMessage();
        Assert.True(
            message.Contains("System.Collections.Stack", StringComparison.Ordinal),
            $"{testCase}: {message}");
    }

    private static string Source(string operation)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System;
                using System.Collections;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("non-generic-stack-construction")]
                public sealed class NonGenericStackConstructionKernel : IEventKernel<ICollection>
                {
                    public bool ShouldHandle(ICollection e, HookContext context) => true;

                    public void Handle(ICollection e, HookContext context)
                    {
                        {{operation}}
                    }
                }
            }
            """;
}
