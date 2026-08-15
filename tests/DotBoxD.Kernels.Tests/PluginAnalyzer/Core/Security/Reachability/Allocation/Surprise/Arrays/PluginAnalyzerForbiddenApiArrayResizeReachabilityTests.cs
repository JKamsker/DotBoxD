namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiArrayResizeReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_array_resize_static_initializer()
    {
        const string memberDeclarations = """
            private static readonly byte[] Retained = CreateRetainedArray();

            private static byte[] CreateRetainedArray()
            {
                var retained = Array.Empty<byte>();
                Array.Resize(ref retained, int.MaxValue);
                return retained;
            }
            """;

        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(memberDeclarations),
            "DotBoxDPluginAnalyzerArrayResizeReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(
            message.Contains("array allocation", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("System.Array", StringComparison.Ordinal),
            message);
    }

    private static string Source(string memberDeclarations)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("array-resize-reachability")]
                public sealed class ArrayResizeKernel : IEventKernel<string>
                {
                    {{memberDeclarations}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}
