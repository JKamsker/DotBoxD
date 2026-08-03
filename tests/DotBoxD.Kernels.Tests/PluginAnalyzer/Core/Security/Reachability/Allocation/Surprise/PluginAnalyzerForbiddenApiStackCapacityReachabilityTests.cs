namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiStackCapacityReachabilityTests
{
    [Theory]
    [InlineData(
        "Stack<byte> capacity",
        "private static readonly Stack<byte> Retained = new(int.MaxValue);",
        "Retained.Count >= 0",
        "System.Collections.Generic.Stack<byte>")]
    [InlineData(
        "direct System.IO control",
        "private static readonly bool Retained = System.IO.File.Exists(\"/x\");",
        "Retained",
        "System.IO.File")]
    public async Task Reports_unbounded_stack_capacity_static_initializer(
        string testCase,
        string fieldDeclaration,
        string predicate,
        string expectedApi)
    {
        var source = Source(fieldDeclaration, predicate);

        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            source,
            "DotBoxDPluginAnalyzerStackCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(message.Contains(expectedApi, StringComparison.Ordinal), $"{testCase}: {message}");
    }

    private static string Source(string fieldDeclaration, string predicate)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("stack-capacity-reachability")]
                public sealed class StackCapacityKernel : IEventKernel<string>
                {
                    {{fieldDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => {{predicate}};

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

}
