using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookFireAsyncObsoleteContextRegressionTests
{
    [Fact]
    public void FireAsync_extension_rejects_error_obsolete_hook_contexts_before_emitting_sources()
    {
        const string source = """
            using System;
            using DotBoxD.Abstractions;

            namespace Regression.Game;

            [Obsolete("Use NewDamageContext", error: true)]
            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record LegacyDamageContext(int Amount);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Amount);
            """;

        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);
        var focusedDiagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id.StartsWith("DBXK", StringComparison.Ordinal) &&
                diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.GetMessage().Contains("obsolete", StringComparison.OrdinalIgnoreCase) &&
                diagnostic.GetMessage().Contains("LegacyDamageContext", StringComparison.Ordinal))
            .ToArray();
        var generatedObsoleteDiagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id is "CS0618" or "CS0619" &&
                diagnostic.Location.GetLineSpan().Path.Contains(
                    "DotBoxDHookFireAsyncExtensions.g.cs",
                    StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            focusedDiagnostics.Length > 0 && generatedObsoleteDiagnostics.Length == 0,
            "Expected a focused DBXK diagnostic for obsolete hook context 'LegacyDamageContext' and no " +
            "generated-source CS0618/CS0619 diagnostics, but saw:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString())));
    }
}
