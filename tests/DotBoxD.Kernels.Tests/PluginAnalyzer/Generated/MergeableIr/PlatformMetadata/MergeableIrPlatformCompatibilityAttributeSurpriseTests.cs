using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class MergeableIrStepGeneratorTests
{
    [Theory]
    [InlineData("\"https://example.invalid/replacement\"")]
    [InlineData("null")]
    public void Generator_preserves_platform_obsoletion_url(string urlLiteral)
    {
        var result = RunGeneratorAndAssertCompiles($$"""
            using System;
            using System.Runtime.Versioning;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ObsoletedOSPlatform("windows10.0", "use replacement", Url = {{urlLiteral}})]
            public sealed record InputEvent(int Value);

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
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = GeneratedSource(result);
        var attribute = "[global::System.Runtime.Versioning.ObsoletedOSPlatformAttribute(\"windows10.0\", \"use replacement\", Url = " +
            urlLiteral + ")]";
        Assert.Contains(
            attribute +
            "\n    public static global::DotBoxD.Abstractions.IRFunc<global::Sample.InputEvent, global::Sample.OutputEvent> CreateIRFunc()",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            attribute +
            "\n        public static global::Sample.StepPipeline<global::Sample.OutputEvent> Intercept_",
            generated,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("osx", "osx", "macos", "macos")]
    [InlineData("macos", "macos", "OSX", "macos")]
    [InlineData("osx10.0", "osx12.0", "macos11.0", "macos11.0")]
    [InlineData("windows7.0", "windows10.0", "windows8.0", "windows8.0")]
    [InlineData("windows10.0", "windows7.0", "windows8.0", "windows8.0")]
    [InlineData("windows", "windows10.0", "windows", "windows")]
    public void Generator_intersects_platform_aliases_and_version_ranges(
        string firstInputPlatform,
        string secondInputPlatform,
        string outputPlatform,
        string expectedPlatform)
    {
        var result = RunGeneratorAndAssertCompiles($$"""
            using System;
            using System.Runtime.Versioning;
            using DotBoxD.Abstractions;

            namespace Sample;

            [SupportedOSPlatform("{{firstInputPlatform}}")]
            [SupportedOSPlatform("{{secondInputPlatform}}")]
            public sealed record InputEvent(int Value);

            [SupportedOSPlatform("{{outputPlatform}}")]
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
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = GeneratedSource(result);
        var attribute = "[global::System.Runtime.Versioning.SupportedOSPlatformAttribute(\"" + expectedPlatform + "\")]";
        Assert.Contains(
            attribute +
            "\n    public static global::DotBoxD.Abstractions.IRFunc<global::Sample.InputEvent, global::Sample.OutputEvent> CreateIRFunc()",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            attribute +
            "\n        public static global::Sample.StepPipeline<global::Sample.OutputEvent> Intercept_",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_preserves_or_rejects_platform_restricted_ir_body_signatures()
    {
        var result = RunGeneratorAndAssertCompiles("""
            using System;
            using System.Runtime.Versioning;
            using DotBoxD.Abstractions;

            namespace Sample;

            [SupportedOSPlatform("windows")]
            public sealed record WindowsEvent(int Value);

            public sealed record PortableEvent(int Value);

            public sealed class FilterPipeline<T> { }

            public sealed class StepPipeline<T>
            {
                public StepPipeline<T> Where(
                    Func<T, bool> predicate,
                    [IRBodyOf(nameof(predicate))] IRFunc<T, bool>? irPredicate = null)
                    => this;

                public FilterPipeline<T> Filter(
                    [LowerToIr(LoweredPipelineStepKind.Filter)] Func<T, bool> predicate)
                    => new();

                public FilterPipeline<T> Filter(LoweredPipelineStep step)
                    => new();
            }

            public static class Usage
            {
                public static StepPipeline<WindowsEvent> ConfigureWindows(StepPipeline<WindowsEvent> pipeline)
                {
                    pipeline.Filter(item => item.Value > 0);
                    return pipeline.Where(item => item.Value > 0);
                }

                public static StepPipeline<PortableEvent> ConfigurePortable(StepPipeline<PortableEvent> pipeline)
                {
                    pipeline.Filter(item => item.Value > 0);
                    return pipeline.Where(item => item.Value > 0);
                }
            }
            """);

        var dbxkDiagnostics = result.Diagnostics
            .Where(diagnostic => diagnostic.Id.StartsWith("DBXK", StringComparison.Ordinal))
            .ToArray();
        if (dbxkDiagnostics.Length > 0)
        {
            Assert.All(dbxkDiagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
            return;
        }

        var generated = GeneratedSource(result);
        const string supportedAttribute =
            "[global::System.Runtime.Versioning.SupportedOSPlatformAttribute(\"windows\")]";

        Assert.Contains(
            supportedAttribute +
            "\n    public static global::DotBoxD.Abstractions.IRFunc<global::Sample.WindowsEvent, bool> CreateIRFunc()",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            supportedAttribute +
            "\n        public static global::Sample.StepPipeline<global::Sample.WindowsEvent> Intercept_",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            supportedAttribute +
            "\n        public static global::Sample.FilterPipeline<global::Sample.WindowsEvent> Intercept_",
            generated,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            supportedAttribute +
            "\n    public static global::DotBoxD.Abstractions.IRFunc<global::Sample.PortableEvent, bool> CreateIRFunc()",
            generated,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            supportedAttribute +
            "\n        public static global::Sample.StepPipeline<global::Sample.PortableEvent> Intercept_",
            generated,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            supportedAttribute +
            "\n        public static global::Sample.FilterPipeline<global::Sample.PortableEvent> Intercept_",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_rejects_mergeable_ir_calls_without_a_shared_supported_platform()
    {
        var result = RunGenerator("""
            using System;
            using System.Runtime.Versioning;
            using DotBoxD.Abstractions;

            namespace Sample;

            [SupportedOSPlatform("windows")]
            public sealed record WindowsEvent(int Value);

            [SupportedOSPlatform("linux")]
            public sealed record LinuxResult(int Value);

            public sealed class StepPipeline<T>
            {
                public StepPipeline<TNext> Select<TNext>(
                    Func<T, TNext> selector,
                    [IRBodyOf(nameof(selector))] IRFunc<T, TNext>? irSelector = null)
                    => new();
            }

            public static class Usage
            {
                public static StepPipeline<LinuxResult> Configure(StepPipeline<WindowsEvent> pipeline)
                    => pipeline.Select(item => new LinuxResult(item.Value));
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(item => item.Id.StartsWith("DBXK", StringComparison.Ordinal)));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("do not share a supported platform", diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}
