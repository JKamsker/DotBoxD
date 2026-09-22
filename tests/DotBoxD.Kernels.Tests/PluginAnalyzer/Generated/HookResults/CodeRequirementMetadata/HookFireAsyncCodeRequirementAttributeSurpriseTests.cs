using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookFireAsyncCodeRequirementAttributeSurpriseTests
{
    [Fact]
    public void Generated_fire_async_extension_preserves_context_code_requirement_attributes()
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
            "[global::System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute(\"Uses reflection.\")]\n" +
            "    public static global::System.Threading.Tasks.ValueTask<global::Regression.Game.TrimmedDamageResult?> " +
            "FireAsync(",
            fireAsyncSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute(\"Generates code.\")]\n" +
            "    public static global::System.Threading.Tasks.ValueTask<global::Regression.Game.DynamicDamageResult?> " +
            "FireAsync(",
            fireAsyncSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[global::System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute(\"Uses reflection.\")]\n" +
            "    public static global::System.Threading.Tasks.ValueTask<global::Regression.Game.PortableDamageResult?> " +
            "FireAsync(",
            fireAsyncSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[global::System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute(\"Generates code.\")]\n" +
            "    public static global::System.Threading.Tasks.ValueTask<global::Regression.Game.PortableDamageResult?> " +
            "FireAsync(",
            fireAsyncSource,
            StringComparison.Ordinal);
    }

    private const string Source = """
        using System.Diagnostics.CodeAnalysis;
        using DotBoxD.Abstractions;

        namespace Regression.Game;

        [RequiresUnreferencedCode("Uses reflection.")]
        [Hook("combat.trimmed", typeof(TrimmedDamageResult))]
        public sealed record TrimmedDamageContext(int Amount);

        [HookResult]
        public readonly partial record struct TrimmedDamageResult(bool Success, string? Reason, int Amount);

        [RequiresDynamicCode("Generates code.")]
        [Hook("combat.dynamic", typeof(DynamicDamageResult))]
        public sealed record DynamicDamageContext(int Amount);

        [HookResult]
        public readonly partial record struct DynamicDamageResult(bool Success, string? Reason, int Amount);

        [Hook("combat.portable", typeof(PortableDamageResult))]
        public sealed record PortableDamageContext(int Amount);

        [HookResult]
        public readonly partial record struct PortableDamageResult(bool Success, string? Reason, int Amount);
        """;
}
