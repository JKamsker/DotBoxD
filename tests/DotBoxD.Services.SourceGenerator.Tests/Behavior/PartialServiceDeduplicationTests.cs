using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Tests.Behavior;

public sealed class PartialServiceDeduplicationTests
{
    [Fact]
    public void Attributed_partial_declarations_emit_one_service_bundle()
    {
        const string firstPart = """
            using DotBoxD.Services.Attributes;

            namespace PartialService;

            [RpcService]
            public partial interface IControlService
            {
                void Start();
            }
            """;
        const string secondPart = """
            using DotBoxD.Services.Attributes;

            namespace PartialService;

            [RpcService]
            public partial interface IControlService
            {
                void Stop();
            }
            """;
        var compilation = GeneratorTestHelper.CreateCompilation(firstPart, secondPart);

        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var result = driver.GetRunResult();
        var generated = result.Results.Single().GeneratedSources;

        result.Diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "CS8785");
        generated.Should().ContainSingle(source =>
            source.HintName == "PartialService_IControlService.DotBoxDRpcProxy.g.cs");
        generated.Should().ContainSingle(source =>
            source.HintName == "PartialService_IControlService.DotBoxDRpcDispatcher.g.cs");
        generated.Should().ContainSingle(source => source.HintName == "DotBoxDGenerated.g.cs");
    }
}
