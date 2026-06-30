using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public sealed class UnsupportedDtoPayloadShapeTests
{
    [Fact]
    public void DtoMembersWithUnsupportedPayloadShapes_ProduceDBXS002_AndSkipDispatch()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.IO;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedDtoPayloads
            {
                public sealed record ObjectRequest(object Value);

                public sealed class StreamRequest
                {
                    public Stream Body = Stream.Null;
                }

                public class BaseTaskResponse
                {
                    public Task<int> Work { get; init; } = Task.FromResult(0);
                }

                public sealed class TaskResponse : BaseTaskResponse
                {
                    public int Id { get; init; }
                }

                [DotBoxDService]
                public interface IDtoPayloads
                {
                    Task<int> SendObjectAsync(ObjectRequest request);
                    Task<int> SendStreamAsync(StreamRequest request);
                    Task<TaskResponse> GetTaskResponseAsync();
                }
            }
            """;

        var runResult = Compile(source);

        var diagnostics = runResult.Diagnostics.Where(d => d.Id == "DBXS002").ToArray();
        diagnostics.Should().HaveCount(3);
        diagnostics.Should().Contain(d => d.GetMessage().Contains("object or dynamic"));
        diagnostics.Should().Contain(d => d.GetMessage().Contains("streaming or control type"));
        diagnostics.Should().Contain(d => d.GetMessage().Contains("Task or ValueTask"));

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName.EndsWith("IDtoPayloads.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("SendObjectAsync(global::Regress.UnsupportedDtoPayloads.ObjectRequest request)");
        proxy.Should().Contain("SendStreamAsync(global::Regress.UnsupportedDtoPayloads.StreamRequest request)");
        proxy.Should().Contain("GetTaskResponseAsync()");
        proxy.Should().Contain("throw new global::System.NotSupportedException");

        var dispatcher = generated
            .Single(g => g.HintName.EndsWith("IDtoPayloads.DotBoxDRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"SendObjectAsync\":");
        dispatcher.Should().NotContain("case \"SendStreamAsync\":");
        dispatcher.Should().NotContain("case \"GetTaskResponseAsync\":");
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
