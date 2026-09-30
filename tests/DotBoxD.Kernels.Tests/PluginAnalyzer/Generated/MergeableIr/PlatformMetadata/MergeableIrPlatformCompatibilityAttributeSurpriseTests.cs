using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class MergeableIrStepGeneratorTests
{
    [Fact]
    public void Generator_preserves_or_rejects_platform_restricted_ir_body_signatures()
    {
        var result = RunGenerator("""
            using System;
            using System.Runtime.Versioning;
            using DotBoxD.Abstractions;

            namespace Sample;

            [SupportedOSPlatform("windows")]
            public sealed record WindowsEvent(int Value);

            public sealed record PortableEvent(int Value);

            public sealed class StepPipeline<T>
            {
                public StepPipeline<T> Where(
                    Func<T, bool> predicate,
                    [IRBodyOf(nameof(predicate))] IRFunc<T, bool>? irPredicate = null)
                    => this;
            }

            public static class Usage
            {
                public static StepPipeline<WindowsEvent> ConfigureWindows(StepPipeline<WindowsEvent> pipeline)
                    => pipeline.Where(item => item.Value > 0);

                public static StepPipeline<PortableEvent> ConfigurePortable(StepPipeline<PortableEvent> pipeline)
                    => pipeline.Where(item => item.Value > 0);
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
    }
}
