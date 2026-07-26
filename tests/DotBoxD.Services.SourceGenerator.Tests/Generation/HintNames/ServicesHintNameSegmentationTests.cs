using FluentAssertions;
using Microsoft.CodeAnalysis;
using static DotBoxD.Services.SourceGenerator.Tests.Generation.CodegenRegressionTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class ServicesHintNameSegmentationTests
{
    [Fact]
    public void NamespaceAndInterfaceBoundaryAmbiguity_DoesNotCollideOnHintNames()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace A
            {
                [RpcService(Name = "A.I_B_C")]
                public interface I_B_C
                {
                    Task<int> FromFlatAsync();
                }
            }

            namespace A.I
            {
                [RpcService(Name = "A.I.B_C")]
                public interface B_C
                {
                    Task<int> FromNestedAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);

        runResult.Diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "CS8785");
        AssertCompiles(final);

        var generated = runResult.Results.Single().GeneratedSources;
        generated.Should().ContainSingle(source =>
            IsProxy(source) && ContainsServiceInterface(source, "global::A.I_B_C"));
        generated.Should().ContainSingle(source =>
            IsProxy(source) && ContainsServiceInterface(source, "global::A.I.B_C"));
        generated.Should().ContainSingle(source =>
            IsDispatcher(source) && ContainsServiceInterface(source, "global::A.I_B_C"));
        generated.Should().ContainSingle(source =>
            IsDispatcher(source) && ContainsServiceInterface(source, "global::A.I.B_C"));
    }

    private static bool IsProxy(GeneratedSourceResult source) =>
        source.HintName.EndsWith(".DotBoxDRpcProxy.g.cs", StringComparison.Ordinal);

    private static bool IsDispatcher(GeneratedSourceResult source) =>
        source.HintName.EndsWith(".DotBoxDRpcDispatcher.g.cs", StringComparison.Ordinal);

    private static bool ContainsServiceInterface(GeneratedSourceResult source, string qualifiedName) =>
        source.SourceText.ToString().Contains(qualifiedName, StringComparison.Ordinal);
}
