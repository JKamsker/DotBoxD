using System.Globalization;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class MergeableIrStepGeneratorTests
{
    [Fact]
    public void Generator_lowers_ir_body_parameters_to_ir_func_arguments()
    {
        var result = RunGeneratorAndAssertCompiles("""
            using System;
            using System.Collections.Generic;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;

            namespace Sample;

            public sealed record ProbeEvent([property: Capability("probe.read.distance")] int Distance, string TargetId);

            public sealed class StepPipeline<T>
            {
                private readonly List<LoweredPipelineStep> _steps = new();

                public StepPipeline<T> Where(
                    Func<T, bool> predicate,
                    [IRBodyOf(nameof(predicate))] IRFunc<T, bool>? irPredicate = null)
                {
                    _steps.Add((irPredicate ?? throw new InvalidOperationException("not lowered")).Step);
                    return this;
                }

                public StepPipeline<TNext> Select<TNext>(
                    Func<T, TNext> selector,
                    [IRBodyOf(nameof(selector))] IRFunc<T, TNext>? irSelector = null)
                {
                    _steps.Add((irSelector ?? throw new InvalidOperationException("not lowered")).Step);
                    return new StepPipeline<TNext>();
                }
            }

            public static class Usage
            {
                public static StepPipeline<string> Configure(StepPipeline<ProbeEvent> pipeline)
                    => pipeline.Where(e => e.Distance >= 4).Select(e => e.TargetId);
            }
            """);

        var generated = GeneratedSource(result);

        Assert.Contains("CreateIRFunc()", generated, StringComparison.Ordinal);
        Assert.Contains("IRFunc<global::Sample.ProbeEvent, bool>", generated, StringComparison.Ordinal);
        Assert.Contains("LoweredPipelineStepKind.Filter", generated, StringComparison.Ordinal);
        Assert.Contains("LoweredPipelineStepKind.Projection", generated, StringComparison.Ordinal);
        Assert.Contains("@predicate: @predicate", generated, StringComparison.Ordinal);
        Assert.Contains("@irPredicate:", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_supports_explicit_projection_kind_for_bool_ir_func()
    {
        var result = RunGeneratorAndAssertCompiles("""
            using System;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;

            namespace Sample;

            public sealed record ProbeEvent([property: Capability("probe.read.distance")] int Distance);

            public sealed class StepPipeline<T>
            {
                public StepPipeline<bool> ProjectFlag(
                    Func<T, bool> selector,
                    [IRBodyOf(nameof(selector), LoweredPipelineStepKind.Projection)] IRFunc<T, bool>? irSelector = null)
                    => new();
            }

            public static class Usage
            {
                public static StepPipeline<bool> Configure(StepPipeline<ProbeEvent> pipeline)
                    => pipeline.ProjectFlag(e => e.Distance >= 4);
            }
            """);

        var generated = GeneratedSource(result);

        Assert.Contains("LoweredPipelineStepKind.Projection", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("LoweredPipelineStepKind.Filter", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_supports_named_explicit_null_ir_argument()
    {
        var result = RunGeneratorAndAssertCompiles("""
            using System;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;

            namespace Sample;

            public sealed record ProbeEvent([property: Capability("probe.read.distance")] int Distance);

            public sealed class StepPipeline<T>
            {
                public StepPipeline<T> Where(
                    Func<T, bool> predicate,
                    [IRBodyOf(nameof(predicate))] IRFunc<T, bool>? irPredicate = null)
                    => this;
            }

            public static class Usage
            {
                public static StepPipeline<ProbeEvent> Configure(StepPipeline<ProbeEvent> pipeline)
                    => pipeline.Where(predicate: e => e.Distance >= 4, irPredicate: null);
            }
            """);

        var generated = GeneratedSource(result);

        Assert.Contains("IRFunc<global::Sample.ProbeEvent, bool>? @irPredicate = null", generated, StringComparison.Ordinal);
        Assert.Contains("@irPredicate:", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_respects_explicit_manual_ir_func_argument()
    {
        var result = RunGeneratorAndAssertCompiles("""
            using System;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Model;
            using DotBoxD.Kernels.Sandbox;

            namespace Sample;

            public sealed record ProbeEvent(int Distance);

            public sealed class StepPipeline<T>
            {
                public StepPipeline<T> Where(
                    Func<T, bool> predicate,
                    [IRBodyOf(nameof(predicate))] IRFunc<T, bool>? irPredicate = null)
                    => this;
            }

            public static class Manual
            {
                public static IRFunc<ProbeEvent, bool> AlwaysTrue()
                    => IRFunc<ProbeEvent, bool>.FromStep(new LoweredPipelineStep(
                        LoweredPipelineStepKind.Filter,
                        "record",
                        "bool",
                        Array.Empty<Parameter>(),
                        Array.Empty<Statement>(),
                        new LiteralExpression(SandboxValue.FromBool(true), new SourceSpan(1, 1)),
                        Array.Empty<string>(),
                        Array.Empty<string>()));
            }

            public static class Usage
            {
                public static StepPipeline<ProbeEvent> Configure(StepPipeline<ProbeEvent> pipeline)
                    => pipeline.Where(e => e.Distance >= 4, Manual.AlwaysTrue());
            }
            """);

        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.FilePath.Contains("LoweredPipelineStep_", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_reports_diagnostic_for_missing_ir_body_source_parameter()
        => AssertIRBodyDiagnostic("""
            using System;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class StepPipeline<T>
            {
                public StepPipeline<T> Where(
                    Func<T, bool> predicate,
                    [IRBodyOf("missing")] IRFunc<T, bool>? irPredicate = null)
                    => this;
            }

            public static class Usage
            {
                public static StepPipeline<int> Configure(StepPipeline<int> pipeline)
                    => pipeline.Where(value => value > 0);
            }
            """, "source parameter 'missing' does not exist");

    [Fact]
    public void Generator_reports_diagnostic_for_non_ir_func_body_parameter()
        => AssertIRBodyDiagnostic("""
            using System;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class StepPipeline<T>
            {
                public StepPipeline<T> Where(
                    Func<T, bool> predicate,
                    [IRBodyOf(nameof(predicate))] LoweredPipelineStep? irPredicate = null)
                    => this;
            }

            public static class Usage
            {
                public static StepPipeline<int> Configure(StepPipeline<int> pipeline)
                    => pipeline.Where(value => value > 0);
            }
            """, "parameter must be IRFunc");

    [Fact]
    public void Generator_reports_diagnostic_for_mismatched_ir_func_shape()
        => AssertIRBodyDiagnostic("""
            using System;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class StepPipeline<T>
            {
                public StepPipeline<T> Where(
                    Func<T, int> predicate,
                    [IRBodyOf(nameof(predicate))] IRFunc<T, bool>? irPredicate = null)
                    => this;
            }

            public static class Usage
            {
                public static StepPipeline<int> Configure(StepPipeline<int> pipeline)
                    => pipeline.Where(value => value);
            }
            """, "source delegate type must match");

    [Fact]
    public void Generator_reports_diagnostic_for_required_ir_func_parameter()
        => AssertIRBodyDiagnostic("""
            using System;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class StepPipeline<T>
            {
                public StepPipeline<T> Where(
                    Func<T, bool> predicate,
                    [IRBodyOf(nameof(predicate))] IRFunc<T, bool>? irPredicate)
                    => this;
            }

            public static class Usage
            {
                public static StepPipeline<int> Configure(StepPipeline<int> pipeline)
                    => pipeline.Where(value => value > 0, null);
            }
            """, "must be optional with a null default value");

    private static void AssertIRBodyDiagnostic(string source, string expectedMessage)
    {
        var result = RunGenerator(source);

        var diagnostic = Assert.Single(result.Diagnostics, candidate => candidate.Id == "DBXK100");
        Assert.Contains(
            expectedMessage,
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.FilePath.Contains("LoweredPipelineStep_", StringComparison.Ordinal));
    }
}
