namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiListCapacitySetterReachabilityTests
{
    [Theory]
    [InlineData(
        "List<byte>.Capacity setter",
        """
        private static readonly List<byte> Retained = CreateRetainedList();

        private static List<byte> CreateRetainedList()
        {
            var retained = new List<byte>();
            retained.Capacity = int.MaxValue;
            return retained;
        }
        """,
        "System.Collections.Generic.List")]
    [InlineData(
        "direct System.IO control",
        "private static readonly bool Retained = System.IO.File.Exists(\"plugin.txt\");",
        "System.IO.File")]
    public async Task Reports_unbounded_list_capacity_setter_static_initializer(
        string testCase,
        string staticMembers,
        string expectedApi)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(staticMembers),
            "DotBoxDPluginAnalyzerListCapacitySetterReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(
            message.Contains(expectedApi, StringComparison.Ordinal),
            $"{testCase}: {message}");
    }

    private static string Source(string staticMembers)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("list-capacity-setter-reachability")]
                public sealed class ListCapacitySetterKernel : IEventKernel<string>
                {
                    {{staticMembers}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}
