using FluentAssertions;

using static DotBoxD.Services.SourceGenerator.Tests.Generation.CodegenRegressionTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class PlatformCompatibilityAttributeMetadataTests
{
    [Fact]
    public void SupportedOSPlatformAttribute_IsPreservedOnGeneratedServiceMembers()
    {
        var (_, runResult) = RunWithPreviewByRefLikeGenerics(PlatformRestrictedServiceSource);

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.PlatformCompatibility", "IPlatformRestricted", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString()
            .Replace("\r\n", "\n");
        proxy.Should().Contain(
            "[global::System.Runtime.Versioning.SupportedOSPlatformAttribute(\"windows\")]\n" +
            "        public global::System.Threading.Tasks.Task WindowsOnlyAsync()");

        var asyncSibling = generated
            .Single(g => g.HintName.EndsWith("IPlatformRestricted.DotBoxDRpcAsync.g.cs", StringComparison.Ordinal))
            .SourceText.ToString()
            .Replace("\r\n", "\n");
        asyncSibling.Should().Contain(
            "[global::System.Runtime.Versioning.SupportedOSPlatformAttribute(\"windows\")]\n" +
            "        global::System.Threading.Tasks.Task WindowsOnlyAsync(");
    }

    private const string PlatformRestrictedServiceSource = """
        using DotBoxD.Services.Attributes;
        using System.Runtime.Versioning;
        using System.Threading.Tasks;

        namespace Regress.PlatformCompatibility
        {
            [RpcService]
            public interface IPlatformRestricted
            {
                [SupportedOSPlatform("windows")]
                Task WindowsOnlyAsync();
            }
        }
        """;
}
