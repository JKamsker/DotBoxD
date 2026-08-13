namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiHashSetEnsureCapacityReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_hash_set_ensure_capacity_static_initializer()
    {
        const string memberDeclarations = """
            private static readonly HashSet<byte> Retained = CreateRetainedHashSet();

            private static HashSet<byte> CreateRetainedHashSet()
            {
                var retained = new HashSet<byte>();
                retained.EnsureCapacity(int.MaxValue);
                return retained;
            }
            """;

        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(memberDeclarations),
            "DotBoxDPluginAnalyzerHashSetEnsureCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.Contains("System.Collections.Generic.HashSet", message, StringComparison.Ordinal);
    }

    private static string Source(string memberDeclarations)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("hash-set-ensure-capacity-reachability")]
                public sealed class HashSetEnsureCapacityKernel : IEventKernel<string>
                {
                    {{memberDeclarations}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}
