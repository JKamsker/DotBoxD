using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RegistrationAccumulatorEnclosingTypeCollisionTests
{
    [Fact]
    public void Method_named_like_target_accumulator_type_reports_dbxk100()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [GeneratePluginRegistrationAccumulator("RegisterAsync", "RegisterAsync")]
            internal sealed class RemoteServiceControl
            {
                public ValueTask<int> RegisterAsync()
                    => ValueTask.FromResult(1);
            }
            """);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("RegisterAsync", StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains("collid", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CS0542");
    }
}
