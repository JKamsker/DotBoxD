using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerHookAttributeNameGrammarSurpriseTests
{
    [Theory]
    [InlineData("bad..hook")]
    [InlineData("bad hook")]
    [InlineData("bad\u0001hook")]
    public void Malformed_hook_name_reports_DBXK100_without_generated_metadata(string hookName)
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(Source(hookName));

        Assert.Contains(
            result.Diagnostics,
            diagnostic =>
                diagnostic.Id == "DBXK100" &&
                diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.GetMessage().Contains("[Hook] name", StringComparison.Ordinal) &&
                diagnostic.GetMessage().Contains("dot-separated identifier", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.GetText().ToString().Contains(hookName, StringComparison.Ordinal));
    }

    private static string Source(string hookName)
        => $$"""
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            namespace Sample;

            [Hook("{{Escape(hookName)}}", typeof(DamageResult))]
            public sealed record DamageEvent(string TargetId, string Message);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason);

            [Plugin("malformed-hook-name")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """;

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\u0001", "\\u0001", StringComparison.Ordinal);
}
