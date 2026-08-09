using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RegistrationAccumulatorInheritedMethodSurpriseTests
{
    [Fact]
    public void Inherited_public_registration_method_generates_accumulator()
    {
        var generated = string.Join("\n", PluginAnalyzerGeneratedPackageFactory.GeneratedSources("""
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            public class ControlBase
            {
                public ValueTask<string> Replace() => ValueTask.FromResult("replaced");
            }

            [GeneratePluginRegistrationAccumulator("ControlAccumulator", "Replace")]
            public sealed class Control : ControlBase
            {
            }
            """));

        Assert.Contains("ControlAccumulator", generated, StringComparison.Ordinal);
        Assert.Contains("Replace()", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Direct_public_registration_method_remains_supported()
    {
        var generated = string.Join("\n", PluginAnalyzerGeneratedPackageFactory.GeneratedSources("""
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [GeneratePluginRegistrationAccumulator("ControlAccumulator", "Replace")]
            public sealed class Control
            {
                public ValueTask<string> Replace() => ValueTask.FromResult("replaced");
            }
            """));

        Assert.Contains("ControlAccumulator", generated, StringComparison.Ordinal);
        Assert.Contains("Replace()", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Hidden_registration_methods_remain_rejected_as_ambiguous()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            public class ControlBase
            {
                public ValueTask<string> Replace() => ValueTask.FromResult("base");
            }

            [GeneratePluginRegistrationAccumulator("ControlAccumulator", "Replace")]
            public sealed class Control : ControlBase
            {
                public new ValueTask<string> Replace() => ValueTask.FromResult("derived");
            }
            """);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("Replace", StringComparison.Ordinal));
    }
}
