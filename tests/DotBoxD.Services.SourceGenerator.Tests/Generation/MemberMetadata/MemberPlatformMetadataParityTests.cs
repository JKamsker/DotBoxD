using FluentAssertions;
using Microsoft.CodeAnalysis;

using static DotBoxD.Services.SourceGenerator.Tests.Generation.CodegenRegressionTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class MemberPlatformMetadataParityTests
{
    [Theory]
    [InlineData("SupportedOSPlatform(\"windows10.0\")", false)]
    [InlineData("SupportedOSPlatform(\"windows10.0\")", true)]
    [InlineData("UnsupportedOSPlatform(\"browser\", \"Use another API\")", false)]
    [InlineData("UnsupportedOSPlatform(\"browser\", \"Use another API\")", true)]
    [InlineData("ObsoletedOSPlatform(\"windows10.0\", \"Use another API\", Url = \"https://example.test/platform\")", false)]
    [InlineData("ObsoletedOSPlatform(\"windows10.0\", \"Use another API\", Url = \"https://example.test/platform\")", true)]
    public void Generated_members_preserve_platform_metadata(string attribute, bool inherited)
    {
        var (final, _) = Run($$"""
            using DotBoxD.Services.Attributes;
            using System.Runtime.Versioning;
            using System.Threading.Tasks;

            namespace PlatformParity;
            [RpcService]
            public interface IChild { Task PingAsync(); }

            {{(inherited ? "" : "[RpcService]")}}
            public interface {{(inherited ? "IBase" : "IRoot")}}
            {
                [{{attribute}}]
                Task CallAsync();
                [{{attribute}}]
                IChild Child { get; }
            }
            {{(inherited ? "[RpcService] public interface IRoot : IBase { }" : "")}}
            """);
        AssertCompiles(final);

        var original = final.GetTypeByMetadataName(inherited ? "PlatformParity.IBase" : "PlatformParity.IRoot")!;
        foreach (var generatedType in new[] { "PlatformParity.RootProxy", "PlatformParity.IRootAsync" })
        {
            var generated = final.GetTypeByMetadataName(generatedType);
            generated.Should().NotBeNull();
            foreach (var name in generatedType.EndsWith("Proxy", StringComparison.Ordinal)
                ? new[] { "CallAsync", "Child" } : new[] { "CallAsync" })
            {
                var expected = original.GetMembers(name).Single().GetAttributes().Single();
                var members = generated!.GetMembers(name);
                members.Should().NotBeEmpty();
                foreach (var member in members)
                {
                    var actual = member.GetAttributes().Should().ContainSingle(a =>
                        SymbolEqualityComparer.Default.Equals(a.AttributeClass, expected.AttributeClass)).Subject;
                    actual.ConstructorArguments.Select(a => a.Value).Should()
                        .Equal(expected.ConstructorArguments.Select(a => a.Value));
                    actual.NamedArguments.Select(a => (a.Key, a.Value.Value)).Should()
                        .Equal(expected.NamedArguments.Select(a => (a.Key, a.Value.Value)));
                }
            }
        }
    }
}
