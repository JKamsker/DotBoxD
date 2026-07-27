using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public sealed class JsonIgnoreConditionDtoPayloadTests
{
    [Fact]
    public void JsonIgnoreConditionNeverMembers_AreValidatedAsWireVisiblePayload()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;

            namespace Regress.JsonIgnoreCondition
            {
                public sealed class UnconditionalJsonIgnoredRequest
                {
                    public int Value { get; init; }

                    [JsonIgnore]
                    public Task<int> Work { get; init; } = Task.FromResult(0);
                }

                public sealed class NeverJsonIgnoredRequest
                {
                    public int Value { get; init; }

                    [JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
                    public Task<int> Work { get; init; } = Task.FromResult(0);
                }

                [RpcService]
                public interface IJsonIgnoreConditionRpc
                {
                    Task<int> SendIgnoredAsync(UnconditionalJsonIgnoredRequest request);
                    Task<int> SendNeverAsync(NeverJsonIgnoredRequest request);
                }
            }
            """;

        var runResult = Compile(source);

        var diagnostics = runResult.Diagnostics.Where(d => d.Id == "DBXS002").ToArray();
        diagnostics.Should().ContainSingle();
        diagnostics[0].GetMessage().Should().Contain("SendNeverAsync");
        diagnostics[0].GetMessage().Should().Contain("Task or ValueTask");

        var dispatcher = runResult.Results.Single()
            .GeneratedSources
            .Single(g => g.HintName.EndsWith("IJsonIgnoreConditionRpc.DotBoxDRpcDispatcher.g.cs"))
            .SourceText
            .ToString();
        dispatcher.Should().Contain("case \"SendIgnoredAsync\":");
        dispatcher.Should().NotContain("case \"SendNeverAsync\":");
    }

    private static GeneratorDriverRunResult Compile(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));

        return runResult;
    }
}
