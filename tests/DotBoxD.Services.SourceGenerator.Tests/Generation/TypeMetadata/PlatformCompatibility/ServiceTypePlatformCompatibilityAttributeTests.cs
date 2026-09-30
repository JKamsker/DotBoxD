using FluentAssertions;
using Microsoft.CodeAnalysis;

using static DotBoxD.Services.SourceGenerator.Tests.Generation.CodegenRegressionTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class ServiceTypePlatformCompatibilityAttributeTests
{
    private const string SupportedOsPlatformAttribute =
        "[global::System.Runtime.Versioning.SupportedOSPlatformAttribute(\"windows\")]";

    [Fact]
    public void PlatformRestrictedServices_PreservePlatformCompatibilityOnGeneratedTypes()
    {
        var (_, runResult) = RunWithPreviewByRefLikeGenerics(ServiceSource);
        var generated = runResult.Results.Single().GeneratedSources;

        var platformProxy = SourceFor(
            generated,
            "Regress.ServiceTypePlatformCompatibility",
            "IPlatformService",
            GeneratorTestHelper.GeneratedKind.Proxy);
        var platformAsyncSibling = SourceFor(
            generated,
            "Regress.ServiceTypePlatformCompatibility",
            "IPlatformService",
            GeneratorTestHelper.GeneratedKind.Async);
        var platformDispatcher = SourceFor(
            generated,
            "Regress.ServiceTypePlatformCompatibility",
            "IPlatformService",
            GeneratorTestHelper.GeneratedKind.Dispatcher);
        var portableProxy = SourceFor(
            generated,
            "Regress.ServiceTypePlatformCompatibility",
            "IPortableService",
            GeneratorTestHelper.GeneratedKind.Proxy);
        var portableAsyncSibling = SourceFor(
            generated,
            "Regress.ServiceTypePlatformCompatibility",
            "IPortableService",
            GeneratorTestHelper.GeneratedKind.Async);
        var portableDispatcher = SourceFor(
            generated,
            "Regress.ServiceTypePlatformCompatibility",
            "IPortableService",
            GeneratorTestHelper.GeneratedKind.Dispatcher);

        platformProxy.Should().Contain(
            SupportedOsPlatformAttribute + "\n    public sealed class PlatformServiceProxy :");
        platformAsyncSibling.Should().Contain(
            SupportedOsPlatformAttribute + "\n    public interface IPlatformServiceAsync");
        platformDispatcher.Should().Contain(
            SupportedOsPlatformAttribute + "\n    public sealed class PlatformServiceDispatcher :");

        portableProxy.Should().NotContain(SupportedOsPlatformAttribute);
        portableAsyncSibling.Should().NotContain(SupportedOsPlatformAttribute);
        portableDispatcher.Should().NotContain(SupportedOsPlatformAttribute);
    }

    private static string SourceFor(
        IEnumerable<GeneratedSourceResult> generated,
        string @namespace,
        string interfaceName,
        GeneratorTestHelper.GeneratedKind kind)
        => generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(@namespace, interfaceName, kind))
            .SourceText.ToString()
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    private const string ServiceSource = """
        using DotBoxD.Services.Attributes;
        using System.Runtime.Versioning;
        using System.Threading.Tasks;

        namespace Regress.ServiceTypePlatformCompatibility
        {
            [SupportedOSPlatform("windows")]
            [RpcService]
            public interface IPlatformService
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
