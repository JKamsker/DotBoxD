using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookFireAsyncPreviewFeatureAttributeSurpriseTests
{
    [Fact]
    public void Generated_fire_async_extension_preserves_context_preview_feature_attribute()
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
            "[global::System.Runtime.Versioning.RequiresPreviewFeaturesAttribute(\"Preview combat contract.\", " +
            "Url = \"https://example.test/preview-combat\")]\n" +
            "    public static global::System.Threading.Tasks.ValueTask<global::Regression.Game.PreviewDamageResult?> " +
            "FireAsync(",
            fireAsyncSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[global::System.Runtime.Versioning.RequiresPreviewFeaturesAttribute(\"Preview combat contract.\", " +
            "Url = \"https://example.test/preview-combat\")]\n" +
            "    public static global::System.Threading.Tasks.ValueTask<global::Regression.Game.PortableDamageResult?> " +
            "FireAsync(",
            fireAsyncSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Runtime.Versioning.RequiresPreviewFeaturesAttribute]\n" +
            "    public static global::System.Threading.Tasks.ValueTask<global::Regression.Game.DefaultPreviewDamageResult?> " +
            "FireAsync(",
            fireAsyncSource,
            StringComparison.Ordinal);
    }

    private const string Source = """
        using System.Runtime.Versioning;
        using DotBoxD.Abstractions;

        namespace Regression.Game;

        [RequiresPreviewFeatures("Preview combat contract.", Url = "https://example.test/preview-combat")]
        [Hook("combat.preview", typeof(PreviewDamageResult))]
        public sealed record PreviewDamageContext(int Amount);

        [HookResult]
        public readonly partial record struct PreviewDamageResult(bool Success, string? Reason, int Amount);

        [RequiresPreviewFeatures]
        [Hook("combat.preview-default", typeof(DefaultPreviewDamageResult))]
        public sealed record DefaultPreviewDamageContext(int Amount);

        [HookResult]
        public readonly partial record struct DefaultPreviewDamageResult(bool Success, string? Reason, int Amount);

        [Hook("combat.portable", typeof(PortableDamageResult))]
        public sealed record PortableDamageContext(int Amount);

        [HookResult]
        public readonly partial record struct PortableDamageResult(bool Success, string? Reason, int Amount);
        """;
}
