using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookFireAsyncPlatformCompatibilityAttributeSurpriseTests
{
    [Fact]
    public void Generated_fire_async_extension_preserves_context_platform_compatibility_attribute()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(Source);

        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var fireAsyncSource = Assert.Single(
            result.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("DotBoxDHookFireAsyncExtensions.g.cs", StringComparison.Ordinal))
            .GetText()
            .ToString()
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains(
            "[global::System.Runtime.Versioning.SupportedOSPlatformAttribute(\"windows\")]\n" +
            "    public static global::System.Threading.Tasks.ValueTask<global::Regression.Game.WindowsDamageResult?> " +
            "FireAsync(",
            fireAsyncSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[global::System.Runtime.Versioning.SupportedOSPlatformAttribute(\"windows\")]\n" +
            "    public static global::System.Threading.Tasks.ValueTask<global::Regression.Game.PortableDamageResult?> " +
            "FireAsync(",
            fireAsyncSource,
            StringComparison.Ordinal);
    }

    private const string Source = """
        using System.Runtime.Versioning;
        using DotBoxD.Abstractions;

        namespace Regression.Game;

        [SupportedOSPlatform("windows")]
        [Hook("combat.windows", typeof(WindowsDamageResult))]
        public sealed record WindowsDamageContext(int Amount);

        [HookResult]
        public readonly partial record struct WindowsDamageResult(bool Success, string? Reason, int Amount);

        [Hook("combat.portable", typeof(PortableDamageResult))]
        public sealed record PortableDamageContext(int Amount);

        [HookResult]
        public readonly partial record struct PortableDamageResult(bool Success, string? Reason, int Amount);
        """;
}
