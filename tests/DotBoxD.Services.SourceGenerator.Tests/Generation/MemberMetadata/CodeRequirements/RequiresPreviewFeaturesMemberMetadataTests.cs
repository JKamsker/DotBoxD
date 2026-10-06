using FluentAssertions;

using static DotBoxD.Services.SourceGenerator.Tests.Generation.CodegenRegressionTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation.MemberMetadata;

public sealed class RequiresPreviewFeaturesMemberMetadataTests
{
    private const string PreviewAttribute =
        "[global::System.Runtime.Versioning.RequiresPreviewFeaturesAttribute(\"Preview RPC\", Url = \"https://example.test/preview\")]";

    [Fact]
    public void GeneratedServiceMembers_PreservePreviewFeatureRequirements()
    {
        var (_, runResult) = Run(ServiceSource);
        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.PreviewMemberMetadata",
                "IPreviewService",
                GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString()
            .Replace("\r\n", "\n");
        var asyncSibling = generated
            .Single(g => g.HintName.EndsWith("IPreviewService.DotBoxDRpcAsync.g.cs", StringComparison.Ordinal))
            .SourceText.ToString()
            .Replace("\r\n", "\n");

        AssertMemberHasAttribute(proxy, "public global::System.Threading.Tasks.Task PreviewAsync()");
        AssertMemberHasAttribute(
            proxy,
            "public global::System.Threading.Tasks.Task PreviewAsync(global::System.Threading.CancellationToken ct = default)");
        AssertMemberHasAttribute(
            asyncSibling,
            "global::System.Threading.Tasks.Task PreviewAsync(global::System.Threading.CancellationToken ct = default);");
        AssertMemberDoesNotHaveAttribute(proxy, "public global::System.Threading.Tasks.Task ControlAsync()");
        AssertMemberDoesNotHaveAttribute(
            proxy,
            "public global::System.Threading.Tasks.Task ControlAsync(global::System.Threading.CancellationToken ct = default)");
        AssertMemberDoesNotHaveAttribute(
            asyncSibling,
            "global::System.Threading.Tasks.Task ControlAsync(global::System.Threading.CancellationToken ct = default);");
    }

    private static void AssertMemberHasAttribute(string source, string declaration) =>
        source.Should().Contain($"{PreviewAttribute}\n        {declaration}");

    private static void AssertMemberDoesNotHaveAttribute(string source, string declaration)
    {
        var declarationIndex = source.IndexOf(declaration, StringComparison.Ordinal);
        declarationIndex.Should().BeGreaterThanOrEqualTo(0);

        var declarationLineStart = source.LastIndexOf('\n', declarationIndex - 1) + 1;
        var attributeBlockStart = declarationLineStart;
        while (attributeBlockStart > 0)
        {
            var previousLineEnd = attributeBlockStart - 1;
            var previousLineStart = source.LastIndexOf('\n', previousLineEnd - 1) + 1;
            if (!source.Substring(previousLineStart, previousLineEnd - previousLineStart)
                    .Trim()
                    .StartsWith("[", StringComparison.Ordinal))
            {
                break;
            }

            attributeBlockStart = previousLineStart;
        }

        source.Substring(attributeBlockStart, declarationLineStart - attributeBlockStart)
            .Should().NotContain("RequiresPreviewFeaturesAttribute");
    }

    private const string ServiceSource = """
        using DotBoxD.Services.Attributes;
        using System.Runtime.Versioning;
        using System.Threading.Tasks;

        namespace Regress.PreviewMemberMetadata
        {
            [RpcService]
            public interface IPreviewService
            {
                [RequiresPreviewFeatures("Preview RPC", Url = "https://example.test/preview")]
                Task PreviewAsync();

                Task ControlAsync();
            }
        }
        """;
}
