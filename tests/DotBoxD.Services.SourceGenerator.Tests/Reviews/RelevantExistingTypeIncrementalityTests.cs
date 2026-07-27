using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Reviews;

public sealed class RelevantExistingTypeIncrementalityTests
{
    private const string Services = """
        using DotBoxD.Services.Attributes;

        namespace Incremental.RelevantTypes
        {
            [RpcService]
            public interface IFoo
            {
                int Get();
            }

            [RpcService]
            public interface IBar
            {
                int Get();
            }
        }
        """;

    [Theory]
    [InlineData("NoiseAProxy", "NoiseBProxy")]
    [InlineData("NoiseADispatcher", "NoiseBDispatcher")]
    [InlineData("NoiseAAsync", "NoiseBAsync")]
    public void UnmatchedGeneratedLookingRename_KeepsCollisionStagesStrictlyCached(
        string firstName,
        string secondName)
    {
        var serviceTree = CSharpSyntaxTree.ParseText(Services);
        var firstNoise = CSharpSyntaxTree.ParseText(NoiseType(firstName));
        var secondNoise = CSharpSyntaxTree.ParseText(NoiseType(secondName));
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(serviceTree, firstNoise);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var before = OutputSnapshot.Create(driver.GetRunResult());

        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(firstNoise, secondNoise));
        var result = driver.GetRunResult();

        Reasons(result, "ExistingTypes").Should().Equal(IncrementalStepRunReason.Modified);
        AssertOnlyCached(result, "ExistingTypeValidatedServiceResults", expectedCount: 2);
        AssertOnlyCached(result, "FinalRejectionInputs", expectedCount: 2);
        AssertOnlyCached(result, "ServiceResults", expectedCount: 2);
        AssertOnlyCached(result, "Services", expectedCount: 2);
        AssertOnlyCached(result, "ServiceBundles", expectedCount: 2);
        AssertOnlyCached(result, "AllServices", expectedCount: 1);
        AssertOnlyCached(result, "AllServiceMetadata", expectedCount: 1);
        AssertAllSourceOutputsCached(result);
        OutputSnapshot.Create(result).Should().Be(before);
    }

    [Fact]
    public void PersistentCollision_UnrelatedGeneratedLookingRenameKeepsValidationCached()
    {
        var serviceTree = CSharpSyntaxTree.ParseText(Services);
        var collisionTree = CSharpSyntaxTree.ParseText("""
            namespace Incremental.RelevantTypes;
            public sealed class FooProxy { }
            """);
        var firstNoise = CSharpSyntaxTree.ParseText(NoiseType("NoiseAAsync"));
        var secondNoise = CSharpSyntaxTree.ParseText(NoiseType("NoiseBAsync"));
        var compilation = GeneratorTestHelper.CreateCompilation()
            .AddSyntaxTrees(serviceTree, collisionTree, firstNoise);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var before = OutputSnapshot.Create(driver.GetRunResult());

        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(firstNoise, secondNoise));
        var result = driver.GetRunResult();

        Reasons(result, "ExistingTypes").Should().Equal(IncrementalStepRunReason.Modified);
        AssertOnlyCached(result, "ExistingTypeValidatedServiceResults", expectedCount: 2);
        AssertOnlyCached(result, "FinalRejectionInputs", expectedCount: 2);
        AssertOnlyCached(result, "ServiceResults", expectedCount: 2);
        OutputSnapshot.Create(result).Should().Be(before);
        result.Diagnostics.Should().ContainSingle(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("FooProxy", StringComparison.Ordinal));
    }

    [Fact]
    public void ProxyCollisionEnter_InvalidatesOnlyTheMatchingService()
    {
        var serviceTree = CSharpSyntaxTree.ParseText(Services);
        var unrelatedType = CSharpSyntaxTree.ParseText(NoiseType("Noise"));
        var collisionType = CSharpSyntaxTree.ParseText("""
            namespace Incremental.RelevantTypes;
            public sealed class FooProxy { }
            """);
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(serviceTree, unrelatedType);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(unrelatedType, collisionType));
        var result = driver.GetRunResult();

        AssertReasons(result, "ExistingTypeValidatedServiceResults", cached: 1, modified: 1);
        AssertReasons(result, "FinalRejectionInputs", cached: 1, modified: 1);
        AssertReasons(result, "ServiceResults", cached: 1, modified: 1);
        result.Diagnostics.Should().ContainSingle(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("FooProxy", StringComparison.Ordinal));
    }

    [Fact]
    public void AsyncCollisionEnter_LeavesPrimaryValidationCached()
    {
        var serviceTree = CSharpSyntaxTree.ParseText(Services);
        var unrelatedType = CSharpSyntaxTree.ParseText(NoiseType("Noise"));
        var collisionType = CSharpSyntaxTree.ParseText("""
            namespace Incremental.RelevantTypes;
            public interface IFooAsync { }
            """);
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(serviceTree, unrelatedType);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(unrelatedType, collisionType));
        var result = driver.GetRunResult();

        AssertOnlyCached(result, "ExistingTypeValidatedServiceResults", expectedCount: 2);
        AssertReasons(result, "FinalRejectionInputs", cached: 1, modified: 1);
        AssertReasons(result, "ServiceResults", cached: 1, modified: 1);
        result.Diagnostics.Should().ContainSingle(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("IFooAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void ServiceRename_MakesPreExistingTypeRelevantWithoutExistingTypeEdit()
    {
        var originalService = CSharpSyntaxTree.ParseText("""
            using DotBoxD.Services.Attributes;
            namespace Incremental.RelevantTypes;
            [RpcService] public interface IBar { int Get(); }
            """);
        var renamedService = CSharpSyntaxTree.ParseText("""
            using DotBoxD.Services.Attributes;
            namespace Incremental.RelevantTypes;
            [RpcService] public interface IFoo { int Get(); }
            """);
        var existingType = CSharpSyntaxTree.ParseText("""
            namespace Incremental.RelevantTypes;
            public sealed class FooProxy { }
            """);
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(originalService, existingType);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        driver.GetRunResult().Diagnostics.Should().NotContain(d => d.Id == "DBXS003");

        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(originalService, renamedService));
        var result = driver.GetRunResult();

        Reasons(result, "ExistingTypes").Should().Equal(IncrementalStepRunReason.Cached);
        Reasons(result, "ExistingTypeValidatedServiceResults").Should()
            .Contain(IncrementalStepRunReason.Modified);
        result.Diagnostics.Should().ContainSingle(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("FooProxy", StringComparison.Ordinal));
        result.Results.Single().GeneratedSources.Should().NotContain(source =>
            source.HintName.Contains("IFoo.", StringComparison.Ordinal));
    }

    [Fact]
    public void MethodOnlyServiceEdit_KeepsCollisionPreparationValueEqual()
    {
        var originalService = CSharpSyntaxTree.ParseText("""
            using DotBoxD.Services.Attributes;
            namespace Incremental.RelevantTypes;
            [RpcService] public interface IFoo { int Get(); }
            """);
        var editedService = CSharpSyntaxTree.ParseText("""
            using DotBoxD.Services.Attributes;
            namespace Incremental.RelevantTypes;
            [RpcService] public interface IFoo { int Sum(); }
            """);
        var unrelatedExistingType = CSharpSyntaxTree.ParseText(NoiseType("NoiseProxy"));
        var compilation = GeneratorTestHelper.CreateCompilation()
            .AddSyntaxTrees(originalService, unrelatedExistingType);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(originalService, editedService));
        var result = driver.GetRunResult();

        Reasons(result, "ExistingTypes").Should().Equal(IncrementalStepRunReason.Cached);
        Reasons(result, "ExistingTypeValidatedServiceResults").Should()
            .Equal(IncrementalStepRunReason.Modified);
        Reasons(result, "FinalRejectionInputs").Should()
            .Equal(IncrementalStepRunReason.Unchanged);
        result.Results.Single().GeneratedSources.Single(source =>
                source.HintName.Contains("IFoo.DotBoxDRpcProxy", StringComparison.Ordinal))
            .SourceText.ToString().Should().Contain("Sum(").And.NotContain("Get(");
    }

    private static string NoiseType(string name) =>
        $"namespace Incremental.Noise; public sealed class {name} {{ }}";

    private static IncrementalStepRunReason[] Reasons(GeneratorDriverRunResult result, string name) =>
        result.Results.Single().TrackedSteps[name]
            .SelectMany(static step => step.Outputs)
            .Select(static output => output.Reason)
            .ToArray();

    private static void AssertOnlyCached(GeneratorDriverRunResult result, string name, int expectedCount)
    {
        var reasons = Reasons(result, name);
        reasons.Should().HaveCount(expectedCount);
        reasons.Should().OnlyContain(reason => reason == IncrementalStepRunReason.Cached);
    }

    private static void AssertReasons(
        GeneratorDriverRunResult result,
        string name,
        int cached,
        int modified)
    {
        var reasons = Reasons(result, name);
        reasons.Should().HaveCount(cached + modified);
        reasons.Count(reason => reason == IncrementalStepRunReason.Cached).Should().Be(cached);
        reasons.Count(reason => reason == IncrementalStepRunReason.Modified).Should().Be(modified);
    }

    private static void AssertAllSourceOutputsCached(GeneratorDriverRunResult result)
    {
        var reasons = result.Results.Single().TrackedOutputSteps.Values
            .SelectMany(static steps => steps)
            .SelectMany(static step => step.Outputs)
            .Select(static output => output.Reason)
            .ToArray();
        reasons.Should().NotBeEmpty();
        reasons.Should().OnlyContain(reason => reason == IncrementalStepRunReason.Cached);
    }

    private sealed record OutputSnapshot(string Sources, string Diagnostics)
    {
        public static OutputSnapshot Create(GeneratorDriverRunResult result)
        {
            var sources = string.Join(
                "\n---\n",
                result.Results.Single().GeneratedSources
                    .OrderBy(static source => source.HintName, StringComparer.Ordinal)
                    .Select(static source => source.HintName + "\n" + source.SourceText));
            var diagnostics = string.Join(
                "\n",
                result.Diagnostics
                    .OrderBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)
                    .ThenBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
                    .Select(static diagnostic =>
                        diagnostic.Id + "|" + diagnostic.Severity + "|" + diagnostic.GetMessage() + "|" +
                        diagnostic.Location.GetLineSpan()));
            return new OutputSnapshot(sources, diagnostics);
        }
    }
}
