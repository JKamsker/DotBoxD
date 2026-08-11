using System.Collections.Immutable;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerContextContractTests
{
    [Fact]
    public void Aliased_lookalike_host_binding_attribute_does_not_reject_context_member()
    {
        var foreignHostBinding = PluginServerGenerationTestDriver.CompileReference(
                """
                namespace DotBoxD.Abstractions;

                public sealed class HostBindingAttribute : System.Attribute;
                """,
                "ForeignHostBinding")
            .WithAliases(ImmutableArray.Create("ForeignHostBinding"));
        var (generated, outputCompilation, generatorDiagnostics) =
            PluginServerGenerationTestDriver.RunWithDiagnostics(
                MinimalServer(
                    """
                    extern alias ForeignHostBinding;

                    [GeneratePluginServer(Context = typeof(GameContext))]
                    public partial class RemotePluginServer : Sample.Game.IGameWorld;

                    public sealed partial class GameContext
                    {
                        [ForeignHostBinding::DotBoxD.Abstractions.HostBinding]
                        public int Read => 0;
                    }
                    """),
                foreignHostBinding);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Id == "DBXK100");
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("public partial class GameContext", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void DotBoxD_host_binding_attribute_still_rejects_context_member()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(MinimalServer("""
            [GeneratePluginServer(Context = typeof(GameContext))]
            public partial class RemotePluginServer : Sample.Game.IGameWorld;

            public sealed partial class GameContext
            {
                [HostBinding("host.context.read", "sample.read", SandboxEffect.Cpu)]
                public int Read => 0;
            }
            """));

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("must not declare [HostBinding] members", StringComparison.Ordinal));
    }
}
