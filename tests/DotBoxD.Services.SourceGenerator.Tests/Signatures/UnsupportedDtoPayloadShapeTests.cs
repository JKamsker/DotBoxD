using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public sealed class UnsupportedDtoPayloadShapeTests
{
    [Fact]
    public void DtoMembersWithoutPublicSetters_AreDelegatedToConfiguredSerializer()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedDtoPayloads
            {
                public sealed class PrivateInitRequest
                {
                    public int Value { get; private init; }
                }

                public sealed class PrivateSetResponse
                {
                    public int Value { get; private set; }
                }

                [RpcService]
                public interface IDtoAccessors
                {
                    Task<int> SendAsync(PrivateInitRequest request);
                    Task<PrivateSetResponse> GetAsync();
                }
            }
            """;

        var runResult = Compile(source);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS002");

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName.EndsWith("IDtoAccessors.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().NotContain("throw new global::System.NotSupportedException");

        var dispatcher = generated
            .Single(g => g.HintName.EndsWith("IDtoAccessors.DotBoxDRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().Contain("case \"SendAsync\":");
        dispatcher.Should().Contain("case \"GetAsync\":");
    }

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

                [RpcService]
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

    [Fact]
    public void DtoConstructorParametersWithDifferentMemberTypes_AreDelegatedToConfiguredSerializer()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedDtoPayloads
            {
                public sealed class MismatchedConstructorRequest
                {
                    public MismatchedConstructorRequest(string value)
                    {
                        Value = value.Length;
                    }

                    public int Value { get; }
                }

                [RpcService]
                public interface IMismatchedConstructorDto
                {
                    Task<int> SendAsync(MismatchedConstructorRequest request);
                }
            }
            """;

        var runResult = Compile(source);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS002");

        var dispatcher = runResult.Results.Single()
            .GeneratedSources
            .Single(g => g.HintName.EndsWith("IMismatchedConstructorDto.DotBoxDRpcDispatcher.g.cs"))
            .SourceText
            .ToString();
        dispatcher.Should().Contain("case \"SendAsync\":");
    }

    [Fact]
    public void DtoMembersSplitAcrossConstructors_AreDelegatedToConfiguredSerializer()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedDtoPayloads
            {
                public sealed class SplitConstructorRequest
                {
                    public SplitConstructorRequest(int id)
                    {
                        Id = id;
                        Name = "";
                    }

                    public SplitConstructorRequest(string name)
                    {
                        Id = -1;
                        Name = name;
                    }

                    public int Id { get; }
                    public string Name { get; }
                }

                [RpcService]
                public interface ISplitConstructorDto
                {
                    Task<int> SendAsync(SplitConstructorRequest request);
                }
            }
            """;

        var runResult = Compile(source);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS002");

        var dispatcher = runResult.Results.Single()
            .GeneratedSources
            .Single(g => g.HintName.EndsWith("ISplitConstructorDto.DotBoxDRpcDispatcher.g.cs"))
            .SourceText
            .ToString();
        dispatcher.Should().Contain("case \"SendAsync\":");
    }

    [Fact]
    public void DtoInheritedMembersMissingFromDerivedConstructor_AreDelegatedToConfiguredSerializer()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedDtoPayloads
            {
                public class BaseRequest
                {
                    public BaseRequest(int id)
                    {
                        Id = id;
                    }

                    public int Id { get; }
                }

                public sealed class DerivedRequest : BaseRequest
                {
                    public DerivedRequest(string name)
                        : base(-1)
                    {
                        Name = name;
                    }

                    public string Name { get; }
                }

                [RpcService]
                public interface IInheritedConstructorDto
                {
                    Task<int> SendAsync(DerivedRequest request);
                }
            }
            """;

        var runResult = Compile(source);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS002");

        var dispatcher = runResult.Results.Single()
            .GeneratedSources
            .Single(g => g.HintName.EndsWith("IInheritedConstructorDto.DotBoxDRpcDispatcher.g.cs"))
            .SourceText
            .ToString();
        dispatcher.Should().Contain("case \"SendAsync\":");
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
