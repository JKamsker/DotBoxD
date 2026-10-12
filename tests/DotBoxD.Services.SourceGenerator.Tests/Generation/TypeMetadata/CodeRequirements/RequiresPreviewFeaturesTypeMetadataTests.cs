using FluentAssertions;
using Microsoft.CodeAnalysis;

using static DotBoxD.Services.SourceGenerator.Tests.Generation.CodegenRegressionTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class RequiresPreviewFeaturesTypeMetadataTests
{
    private const string PreviewAttribute =
        "[global::System.Runtime.Versioning.RequiresPreviewFeaturesAttribute(\"Preview service contract\", Url = \"https://example.test/preview-service\")]";

    [Fact]
    public void GeneratedServiceTypes_PreservePreviewFeatureRequirements()
    {
        var (_, runResult) = RunWithPreviewByRefLikeGenerics(ServiceSource);
        var generated = runResult.Results.Single().GeneratedSources;

        foreach (var kind in new[]
                 {
                     GeneratorTestHelper.GeneratedKind.Proxy,
                     GeneratorTestHelper.GeneratedKind.Async,
                     GeneratorTestHelper.GeneratedKind.Dispatcher,
                 })
        {
            SourceFor(generated, "IPreviewService", kind).Should().Contain(PreviewAttribute);
            SourceFor(generated, "IPortableService", kind).Should().NotContain(
                "RequiresPreviewFeaturesAttribute");
        }
    }

    private static string SourceFor(
        IEnumerable<GeneratedSourceResult> generated,
        string interfaceName,
        GeneratorTestHelper.GeneratedKind kind)
        => generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.ServiceTypeCodeRequirements", interfaceName, kind))
            .SourceText.ToString()
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    private const string ServiceSource = """
        using DotBoxD.Services.Attributes;
        using System.Runtime.Versioning;
        using System.Threading.Tasks;

        namespace Regress.ServiceTypeCodeRequirements
        {
            [RequiresPreviewFeatures("Preview service contract", Url = "https://example.test/preview-service")]
            [RpcService]
            public interface IPreviewService
            {
                Task PingAsync();
            }

            [RpcService]
            public interface IPortableService
            {
                Task PingAsync();
            }
        }
        """;
}
