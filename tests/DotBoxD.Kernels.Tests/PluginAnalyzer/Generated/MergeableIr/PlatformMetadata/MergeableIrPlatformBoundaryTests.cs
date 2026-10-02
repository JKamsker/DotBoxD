using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class MergeableIrStepGeneratorTests
{
    [Theory]
    [InlineData("[UnsupportedOSPlatform(\"windows\")][SupportedOSPlatform(\"windows10.0\")]",
        "[SupportedOSPlatform(\"linux\")]", "linux")]
    [InlineData("[UnsupportedOSPlatform(\"windows\")][SupportedOSPlatform(\"windows10.0\")]",
        "[SupportedOSPlatform(\"windows8.0\")]", "windows10.0")]
    [InlineData("[SupportedOSPlatform(\"ios\")]",
        "[SupportedOSPlatform(\"maccatalyst\")]", "maccatalyst")]
    [InlineData("[SupportedOSPlatform(\"maccatalyst14.0\")]",
        "[SupportedOSPlatform(\"ios15.0\")]", "maccatalyst15.0")]
    [InlineData("[SupportedOSPlatform(\"ios\")][SupportedOSPlatform(\"maccatalyst15.0\")]",
        "[SupportedOSPlatform(\"maccatalyst14.0\")]", "maccatalyst14.0")]
    [InlineData("[SupportedOSPlatform(\"ios16.0\")]",
        "[UnsupportedOSPlatform(\"maccatalyst\")][SupportedOSPlatform(\"maccatalyst15.0\")]", "ios16.0")]
    [InlineData("[SupportedOSPlatform(\"ios16.0\")]",
        "[UnsupportedOSPlatform(\"maccatalyst\")][SupportedOSPlatform(\"maccatalyst16.0\")]", "ios16.0")]
    public void Generator_intersects_denylist_and_implied_platform_support(
        string inputAttributes,
        string outputAttributes,
        string expectedPlatform)
    {
        var result = RunGeneratorAndAssertCompiles(PlatformBoundarySource(inputAttributes, outputAttributes));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = GeneratedSource(result);
        var expectedAttribute = PlatformAttribute("Supported", expectedPlatform);
        Assert.Contains(expectedAttribute + "\n    public static global::DotBoxD.Abstractions.IRFunc", generated, StringComparison.Ordinal);
        Assert.Contains(expectedAttribute + "\n        public static global::Sample.StepPipeline", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("UnsupportedOSPlatformAttribute", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_preserves_shared_platform_removal_boundaries()
    {
        var result = RunGeneratorAndAssertCompiles(PlatformBoundarySource(
            "[SupportedOSPlatform(\"windows7.0\")][UnsupportedOSPlatform(\"windows10.0\")]",
            "[SupportedOSPlatform(\"windows8.0\")]"));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = GeneratedSource(result);
        Assert.Contains(PlatformAttribute("Supported", "windows8.0"), generated, StringComparison.Ordinal);
        Assert.Contains(PlatformAttribute("Unsupported", "windows10.0"), generated, StringComparison.Ordinal);
        Assert.DoesNotContain(PlatformAttribute("Supported", "windows7.0"), generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_combines_implied_and_explicit_maccatalyst_support()
    {
        var result = RunGeneratorAndAssertCompiles(PlatformBoundarySource(
            "[SupportedOSPlatform(\"ios\")][SupportedOSPlatform(\"maccatalyst15.0\")]",
            "[SupportedOSPlatform(\"maccatalyst14.0\")][UnsupportedOSPlatform(\"maccatalyst15.0\")]"));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = GeneratedSource(result);
        Assert.Contains(PlatformAttribute("Supported", "maccatalyst14.0"), generated, StringComparison.Ordinal);
        Assert.Contains(PlatformAttribute("Unsupported", "maccatalyst15.0"), generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_preserves_denylist_default_support_and_reintroduction()
    {
        var result = RunGeneratorAndAssertCompiles(PlatformBoundarySource(
            "[UnsupportedOSPlatform(\"windows\")][SupportedOSPlatform(\"windows10.0\")]",
            "[UnsupportedOSPlatform(\"linux\")]"));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = GeneratedSource(result);
        Assert.Contains(PlatformAttribute("Unsupported", "windows"), generated, StringComparison.Ordinal);
        Assert.Contains(PlatformAttribute("Supported", "windows10.0"), generated, StringComparison.Ordinal);
        Assert.Contains(PlatformAttribute("Unsupported", "linux"), generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_preserves_final_implied_maccatalyst_removal_without_duplicate_boundaries()
    {
        var result = RunGeneratorAndAssertCompiles(PlatformBoundarySource(
            "[UnsupportedOSPlatform(\"ios10.0\")][SupportedOSPlatform(\"ios12.0\")][UnsupportedOSPlatform(\"ios15.0\")]",
            ""));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = GeneratedSource(result);
        var attributes = PlatformAttribute("Supported", "ios12.0") + "\n    " +
            PlatformAttribute("Unsupported", "ios10.0") + "\n    " + PlatformAttribute("Unsupported", "ios15.0");
        Assert.Contains(attributes + "\n    public static global::DotBoxD.Abstractions.IRFunc", generated, StringComparison.Ordinal);
        Assert.Contains(attributes.Replace("\n    ", "\n        ", StringComparison.Ordinal) +
            "\n        public static global::Sample.StepPipeline", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("maccatalyst", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[SupportedOSPlatform(\"ios\")][UnsupportedOSPlatform(\"maccatalyst\")]",
        "[SupportedOSPlatform(\"ios\")]", "ios")]
    [InlineData("[SupportedOSPlatform(\"ios16.0\")][SupportedOSPlatform(\"maccatalyst14.0\")][UnsupportedOSPlatform(\"maccatalyst15.0\")]",
        "[SupportedOSPlatform(\"ios16.0\")]", "ios16.0")]
    public void Generator_excludes_maccatalyst_when_only_ios_has_common_support(
        string inputAttributes,
        string outputAttributes,
        string expectedIosPlatform)
    {
        var result = RunGeneratorAndAssertCompiles(PlatformBoundarySource(inputAttributes, outputAttributes));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = GeneratedSource(result);
        var expectedAttributes = PlatformAttribute("Supported", expectedIosPlatform) + "\n    " +
            PlatformAttribute("Unsupported", "maccatalyst");
        Assert.Contains(expectedAttributes + "\n    public static global::DotBoxD.Abstractions.IRFunc", generated, StringComparison.Ordinal);
        Assert.Contains(expectedAttributes.Replace("\n    ", "\n        ", StringComparison.Ordinal) +
            "\n        public static global::Sample.StepPipeline", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("SupportedOSPlatformAttribute(\"maccatalyst", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[SupportedOSPlatform(\"windows7.0\")][UnsupportedOSPlatform(\"windows10.0\")]",
        "[SupportedOSPlatform(\"windows11.0\")]", "do not share a supported platform")]
    [InlineData("[UnsupportedOSPlatform(\"windows5.0\")][SupportedOSPlatform(\"windows7.0\")]",
        "[UnsupportedOSPlatform(\"windows9.0\")][SupportedOSPlatform(\"windows11.0\")]", "multiple support intervals")]
    [InlineData("[UnsupportedOSPlatform(\"ios10.0\")][UnsupportedOSPlatform(\"maccatalyst15.0\")]",
        "[SupportedOSPlatform(\"maccatalyst12.0\")]", "do not share a supported platform")]
    [InlineData("[SupportedOSPlatform(\"windows10.0\")][UnsupportedOSPlatform(\"windows10.0\")]",
        "", "do not share a supported platform")]
    [InlineData("[SupportedOSPlatform(\"ios16.0\")]",
        "[UnsupportedOSPlatform(\"maccatalyst\")][SupportedOSPlatform(\"maccatalyst17.0\")]",
        "cannot represent MacCatalyst support alongside implied iOS support")]
    [InlineData("[UnsupportedOSPlatform(\"ios10.0\")][SupportedOSPlatform(\"ios12.0\")][UnsupportedOSPlatform(\"ios20.0\")]",
        "[UnsupportedOSPlatform(\"maccatalyst15.0\")]",
        "cannot represent MacCatalyst support alongside implied iOS support")]
    public void Generator_rejects_empty_or_unrepresentable_platform_intersections(
        string inputAttributes,
        string outputAttributes,
        string expectedMessage)
    {
        var result = RunGenerator(PlatformBoundarySource(inputAttributes, outputAttributes));

        var diagnostic = Assert.Single(result.Diagnostics.Where(item => item.Id.StartsWith("DBXK", StringComparison.Ordinal)));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(expectedMessage, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string PlatformAttribute(string kind, string platform)
        => "[global::System.Runtime.Versioning." + kind + "OSPlatformAttribute(\"" + platform + "\")]";

    private static string PlatformBoundarySource(string inputAttributes, string outputAttributes)
        => $$"""
            using System;
            using System.Runtime.Versioning;
            using DotBoxD.Abstractions;

            namespace Sample;

            {{inputAttributes}}
            public sealed record InputEvent(int Value);

            {{outputAttributes}}
            public sealed record OutputEvent(int Value);

            public sealed class StepPipeline<T>
            {
                public StepPipeline<TNext> Select<TNext>(
                    Func<T, TNext> selector,
                    [IRBodyOf(nameof(selector))] IRFunc<T, TNext>? irSelector = null)
                    => new();
            }

            public static class Usage
            {
                public static StepPipeline<OutputEvent> Configure(StepPipeline<InputEvent> pipeline)
                    => pipeline.Select(item => new OutputEvent(item.Value));
            }
            """;
}
