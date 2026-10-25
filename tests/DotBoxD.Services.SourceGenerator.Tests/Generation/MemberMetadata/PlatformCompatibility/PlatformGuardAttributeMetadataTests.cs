using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static DotBoxD.Services.SourceGenerator.Tests.Generation.CodegenRegressionTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation.MemberMetadata;

public sealed class PlatformGuardAttributeMetadataTests
{
    [Fact]
    public void GeneratedProxyMembers_PreservePlatformGuardAttributes()
    {
        var (final, _) = RunWithPreviewByRefLikeGenerics(ServiceSource);

        var contract = final.GetTypeByMetadataName("Regress.PlatformGuards.IPlatformGuards")!;
        var proxy = final.GetTypeByMetadataName("Regress.PlatformGuards.PlatformGuardsProxy")!;

        AssertPlatformGuardIsPreserved(contract, proxy, "IsWindows");
        AssertPlatformGuardIsPreserved(contract, proxy, "IsNotWindows");
    }

    [Fact]
    public void GuardedCallThroughGeneratedProxy_Compiles()
    {
        var (final, _) = RunWithPreviewByRefLikeGenerics(ServiceSource);
        var callSite = CSharpSyntaxTree.ParseText(
            """
            using System.Runtime.Versioning;

            namespace Regress.PlatformGuards;

            public static class PlatformApis
            {
                [SupportedOSPlatform("windows")]
                public static void WindowsOnly()
                {
                }
            }

            public static class GeneratedSurfaceCallSite
            {
                public static void Call(PlatformGuardsProxy proxy)
                {
                    if (proxy.IsWindows())
                    {
                        PlatformApis.WindowsOnly();
                    }
                }
            }
            """,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "GeneratedSurfaceCallSite.cs");

        AssertCompiles(final.AddSyntaxTrees(callSite));
    }

    private static void AssertPlatformGuardIsPreserved(INamedTypeSymbol contract, INamedTypeSymbol proxy, string methodName)
    {
        var expected = contract.GetMembers(methodName).OfType<IMethodSymbol>().Single()
            .GetAttributes().Single();
        var actual = proxy.GetMembers(methodName).OfType<IMethodSymbol>().Single()
            .GetAttributes().Should().ContainSingle(attribute =>
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, expected.AttributeClass)).Subject;

        actual.ConstructorArguments.Select(argument => argument.Value).Should()
            .Equal(expected.ConstructorArguments.Select(argument => argument.Value));
    }

    private const string ServiceSource = """
        using DotBoxD.Services.Attributes;
        using System.Runtime.Versioning;

        namespace Regress.PlatformGuards;

        [RpcService]
        public interface IPlatformGuards
        {
            [SupportedOSPlatformGuard("windows")]
            bool IsWindows();

            [UnsupportedOSPlatformGuard("windows")]
            bool IsNotWindows();
        }
        """;
}
