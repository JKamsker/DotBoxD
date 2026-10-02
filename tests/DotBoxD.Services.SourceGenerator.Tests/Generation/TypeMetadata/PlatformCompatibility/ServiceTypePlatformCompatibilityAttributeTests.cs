using FluentAssertions;
using Microsoft.CodeAnalysis;

using static DotBoxD.Services.SourceGenerator.Tests.Generation.CodegenRegressionTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class ServiceTypePlatformCompatibilityAttributeTests
{
    private const string SupportedOsPlatformAttribute =
        "[global::System.Runtime.Versioning.SupportedOSPlatformAttribute(\"windows\")]";

    [Theory]
    [InlineData("[SupportedOSPlatform(\"windows7.0\")][UnsupportedOSPlatform(\"windows10.0\")]")]
    [InlineData("[UnsupportedOSPlatform(\"windows\")][SupportedOSPlatform(\"windows10.0\")]")]
    [InlineData("[UnsupportedOSPlatform(\"linux\", \"unavailable\")]")]
    [InlineData("[ObsoletedOSPlatform(\"windows10.0\", \"use replacement\", Url = \"https://example.invalid/replacement\")]")]
    public void PlatformPolicies_PreserveAllBoundariesAndMessagesOnGeneratedTypes(string annotations)
    {
        var source = ServiceSource.Replace("[SupportedOSPlatform(\"windows\")]", annotations, StringComparison.Ordinal);
        var (final, runResult) = RunWithPreviewByRefLikeGenerics(source);
        final.GetDiagnostics().Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var original = final.GetTypeByMetadataName("Regress.ServiceTypePlatformCompatibility.IPlatformService")!;
        var expected = PlatformMetadata(original);

        foreach (var typeName in new[] { "PlatformServiceProxy", "IPlatformServiceAsync", "PlatformServiceDispatcher" })
        {
            var generatedType = final.GetSymbolsWithName(typeName).OfType<INamedTypeSymbol>().Single();
            PlatformMetadata(generatedType).Should().Equal(expected);
        }

        runResult.Diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static string?[] PlatformMetadata(INamedTypeSymbol type)
        => type.GetAttributes()
            .Where(attribute => attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "System.Runtime.Versioning")
            .Select(attribute => attribute.ToString())
            .OrderBy(attribute => attribute, StringComparer.Ordinal)
            .ToArray();

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
