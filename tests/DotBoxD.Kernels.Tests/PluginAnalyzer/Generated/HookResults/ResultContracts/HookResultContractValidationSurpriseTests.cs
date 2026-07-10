using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookResultContractValidationSurpriseTests
{
    [Fact]
    public void Ordinary_package_generation_rejects_hook_result_types_that_cannot_satisfy_the_contract()
    {
        const string source = """
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            namespace Sample;

            [Hook("bad.result", typeof(string))]
            public sealed record BadResultContext(string TargetId, string Message);

            [Plugin("bad-result")]
            public sealed partial class BadResultKernel : IEventKernel<BadResultContext>
            {
                public bool ShouldHandle(BadResultContext e, HookContext ctx) => true;

                public void Handle(BadResultContext e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """;

        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);

        Assert.Contains(
            result.Diagnostics,
            diagnostic =>
                diagnostic.Id == "DBXK112" &&
                diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.GetMessage().Contains("HookAttribute.ResultType", StringComparison.Ordinal) &&
                diagnostic.GetMessage().Contains("hook-result contract", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(result.GeneratedTrees);
    }
}
