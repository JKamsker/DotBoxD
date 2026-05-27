using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

/// <summary>
/// Verifies that the ShaRPC incremental source generator caches downstream value-equal
/// outputs across re-runs, both for trivia-only edits to a service interface and for
/// edits to entirely unrelated files in the same compilation.
/// </summary>
public class IncrementalCacheTests
{
    private const string Service1Default = """
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Demo.Svc
        {
            [ShaRpcService]
            public interface IFooService
            {
                Task<int> AddAsync(int a, int b);
                Task PingAsync();
            }
        }
        """;

    private const string UnrelatedDefault = """
        namespace Demo.Other
        {
            public class Unrelated
            {
                public int X { get; set; }
            }
        }
        """;

    private const string Service2Default = """
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Demo.Svc2
        {
            [ShaRpcService]
            public interface IBarService
            {
                Task<string> EchoAsync(string s);
            }
        }
        """;

    [Fact]
    public void TriviaOnlyEditInsideServiceInterface_KeepsDownstreamStepsCached()
    {
        var service1 = CSharpSyntaxTree.ParseText(Service1Default);
        var compilation = GeneratorTestHelper.CreateCompilation()
            .AddSyntaxTrees(service1);

        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        // Edit: add a comment inside the interface body. Same shape, same symbols.
        var service1WithComment = CSharpSyntaxTree.ParseText("""
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Demo.Svc
            {
                [ShaRpcService]
                public interface IFooService
                {
                    // comment-only change should not invalidate the model
                    Task<int> AddAsync(int a, int b);
                    Task PingAsync();
                }
            }
            """);

        var compilation2 = compilation.ReplaceSyntaxTree(service1, service1WithComment);
        driver = driver.RunGenerators(compilation2);
        var result = driver.GetRunResult();

        // Services (the value-equal model) MUST be cached on trivia-only changes.
        AssertStepIsCachedOrUnchanged(result, "Services");
        AssertStepIsCachedOrUnchanged(result, "AllServices");

        // The downstream SourceOutput steps must produce cached outputs (no regeneration).
        AssertAllSourceOutputsCachedOrUnchanged(result);
    }

    [Fact]
    public void EditUnrelatedFile_DoesNotInvalidateServiceOrAggregateOrSourceOutputs()
    {
        var serviceTree = CSharpSyntaxTree.ParseText(Service1Default);
        var unrelatedTree = CSharpSyntaxTree.ParseText(UnrelatedDefault);

        var compilation = GeneratorTestHelper.CreateCompilation()
            .AddSyntaxTrees(serviceTree, unrelatedTree);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        // Edit unrelated only.
        var unrelatedTree2 = CSharpSyntaxTree.ParseText("""
            namespace Demo.Other
            {
                public class Unrelated
                {
                    public int X { get; set; }
                    public int Y { get; set; } // new property unrelated to any service
                }
            }
            """);

        var compilation2 = compilation.ReplaceSyntaxTree(unrelatedTree, unrelatedTree2);
        driver = driver.RunGenerators(compilation2);
        var result = driver.GetRunResult();

        AssertStepIsCachedOrUnchanged(result, "Services");
        AssertStepIsCachedOrUnchanged(result, "AllServices");
        AssertAllSourceOutputsCachedOrUnchanged(result);
    }

    [Fact]
    public void AddingUnrelatedFile_DoesNotInvalidateExistingServiceOutputs()
    {
        var serviceTree = CSharpSyntaxTree.ParseText(Service1Default);
        var compilation = GeneratorTestHelper.CreateCompilation()
            .AddSyntaxTrees(serviceTree);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        var newUnrelated = CSharpSyntaxTree.ParseText("""
            namespace Demo.Brand.New
            {
                public class Added { public int N { get; set; } }
            }
            """);

        var compilation2 = compilation.AddSyntaxTrees(newUnrelated);
        driver = driver.RunGenerators(compilation2);
        var result = driver.GetRunResult();

        AssertStepIsCachedOrUnchanged(result, "Services");
        AssertStepIsCachedOrUnchanged(result, "AllServices");
        AssertAllSourceOutputsCachedOrUnchanged(result);
    }

    [Fact]
    public void RenamingServiceMethod_InvalidatesServicesAndAggregateAndRegeneratesSources()
    {
        var serviceTree = CSharpSyntaxTree.ParseText(Service1Default);
        var compilation = GeneratorTestHelper.CreateCompilation()
            .AddSyntaxTrees(serviceTree);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        var renamed = CSharpSyntaxTree.ParseText("""
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Demo.Svc
            {
                [ShaRpcService]
                public interface IFooService
                {
                    Task<int> SumAsync(int a, int b);
                    Task PingAsync();
                }
            }
            """);

        var compilation2 = compilation.ReplaceSyntaxTree(serviceTree, renamed);
        driver = driver.RunGenerators(compilation2);
        var result = driver.GetRunResult();

        // The semantic model must change.
        AssertStepHasModifiedOutput(result, "Services");
        AssertStepHasModifiedOutput(result, "AllServices");

        // Generated proxy must contain the new method name.
        var proxy = result.Results.Single().GeneratedSources
            .Single(g => g.HintName == "IFooService.ShaRpcProxy.g.cs")
            .SourceText.ToString();
        proxy.Should().Contain("SumAsync(int a, int b");
        proxy.Should().NotContain("AddAsync(");
    }

    [Fact]
    public void AddingSecondService_KeepsFirstServiceOutputCachedAndInvalidatesAggregate()
    {
        var service1 = CSharpSyntaxTree.ParseText(Service1Default);
        var compilation = GeneratorTestHelper.CreateCompilation()
            .AddSyntaxTrees(service1);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        var service2 = CSharpSyntaxTree.ParseText(Service2Default);
        var compilation2 = compilation.AddSyntaxTrees(service2);
        driver = driver.RunGenerators(compilation2);
        var result = driver.GetRunResult();

        // The first service's per-service SourceOutput must remain cached.
        // We assert: the source for IFooService.ShaRpcProxy.g.cs is present and unchanged in cached form.
        var perServiceOutputs = result.Results.Single().TrackedOutputSteps
            .SelectMany(kvp => kvp.Value)
            .SelectMany(s => s.Outputs)
            .ToArray();

        // At least one new output should appear (the second service files) AND at least one
        // cached output should be present (the first service's outputs are content-equal).
        perServiceOutputs.Any(o => o.Reason == IncrementalStepRunReason.New || o.Reason == IncrementalStepRunReason.Modified)
            .Should().BeTrue("the second service must produce new or modified outputs");
        perServiceOutputs.Any(o => o.Reason == IncrementalStepRunReason.Cached || o.Reason == IncrementalStepRunReason.Unchanged)
            .Should().BeTrue("the first service outputs must remain cached when an unrelated second service is added");

        // The AllServices aggregate MUST be modified because the collection of services changed.
        AssertStepHasModifiedOutput(result, "AllServices");

        // Sanity: both services have generated files.
        var hints = result.Results.Single().GeneratedSources.Select(g => g.HintName).ToArray();
        hints.Should().Contain("IFooService.ShaRpcProxy.g.cs");
        hints.Should().Contain("IBarService.ShaRpcProxy.g.cs");
        hints.Should().Contain("ShaRpcExtensions.g.cs");
    }

    [Fact]
    public void RemovingServiceAttribute_DropsServiceFromOutputs()
    {
        var serviceTree = CSharpSyntaxTree.ParseText(Service1Default);
        var compilation = GeneratorTestHelper.CreateCompilation()
            .AddSyntaxTrees(serviceTree);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        // Run 1 sanity
        driver.GetRunResult().Results.Single().GeneratedSources
            .Should().Contain(g => g.HintName == "IFooService.ShaRpcProxy.g.cs");

        var withoutAttr = CSharpSyntaxTree.ParseText("""
            using System.Threading.Tasks;

            namespace Demo.Svc
            {
                public interface IFooService
                {
                    Task<int> AddAsync(int a, int b);
                    Task PingAsync();
                }
            }
            """);

        var compilation2 = compilation.ReplaceSyntaxTree(serviceTree, withoutAttr);
        driver = driver.RunGenerators(compilation2);
        var result = driver.GetRunResult();

        result.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName == "IFooService.ShaRpcProxy.g.cs");
        result.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName == "IFooService.ShaRpcDispatcher.g.cs");
        // Extensions is not emitted when there are no services.
        result.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName == "ShaRpcExtensions.g.cs");
    }

    // ---------- assertion helpers ----------

    private static void AssertStepIsCachedOrUnchanged(GeneratorDriverRunResult result, string trackingName)
    {
        var steps = result.Results.Single().TrackedSteps;
        steps.Should().ContainKey(trackingName, $"step '{trackingName}' should exist in tracked steps");

        var outputs = steps[trackingName].SelectMany(s => s.Outputs).ToArray();
        outputs.Should().NotBeEmpty($"step '{trackingName}' should have produced at least one output");

        outputs.Should().OnlyContain(
            o => o.Reason == IncrementalStepRunReason.Cached || o.Reason == IncrementalStepRunReason.Unchanged,
            $"step '{trackingName}' must remain cached/unchanged on this edit, but reasons were: {string.Join(", ", outputs.Select(o => o.Reason))}");
    }

    private static void AssertStepHasModifiedOutput(GeneratorDriverRunResult result, string trackingName)
    {
        var steps = result.Results.Single().TrackedSteps;
        steps.Should().ContainKey(trackingName);
        var outputs = steps[trackingName].SelectMany(s => s.Outputs).ToArray();
        outputs.Any(o => o.Reason == IncrementalStepRunReason.Modified
                      || o.Reason == IncrementalStepRunReason.New
                      || o.Reason == IncrementalStepRunReason.Removed)
            .Should().BeTrue($"step '{trackingName}' should have at least one Modified/New/Removed output, but reasons were: {string.Join(", ", outputs.Select(o => o.Reason))}");
    }

    private static void AssertAllSourceOutputsCachedOrUnchanged(GeneratorDriverRunResult result)
    {
        var allOutputs = result.Results.Single().TrackedOutputSteps
            .SelectMany(kvp => kvp.Value.Select(step => (StepName: kvp.Key, Step: step)))
            .SelectMany(t => t.Step.Outputs.Select(o => (t.StepName, o.Reason)))
            .ToArray();

        allOutputs.Should().NotBeEmpty("at least one source output should have been produced previously");

        var nonCached = allOutputs
            .Where(t => t.Reason != IncrementalStepRunReason.Cached && t.Reason != IncrementalStepRunReason.Unchanged)
            .ToArray();

        nonCached.Should().BeEmpty(
            "all source outputs must be cached/unchanged after a no-op edit, but got: " +
            string.Join(", ", nonCached.Select(x => x.StepName + "=" + x.Reason)));
    }
}
