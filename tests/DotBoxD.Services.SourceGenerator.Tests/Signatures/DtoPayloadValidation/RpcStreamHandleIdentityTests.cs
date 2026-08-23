using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public sealed class RpcStreamHandleIdentityTests
{
    [Fact]
    public void ConsumerDefinedRpcStreamHandleDto_IsNotTreatedAsTransportControlType()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace DotBoxD.Services.Protocol
            {
                public sealed class RpcStreamHandle
                {
                    public int Value { get; init; }
                }
            }

            namespace Regress.RpcStreamHandleIdentity
            {
                [RpcService]
                public interface ILookalikeStreamHandleService
                {
                    Task<int> SendAsync(DotBoxD.Services.Protocol.RpcStreamHandle request);
                }
            }
            """;

        var runResult = Compile(source);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS002");

        var dispatcher = runResult.Results.Single()
            .GeneratedSources
            .Single(g => g.HintName.EndsWith("ILookalikeStreamHandleService.DotBoxDRpcDispatcher.g.cs"))
            .SourceText
            .ToString();
        dispatcher.Should().Contain("case \"SendAsync\":");
    }

    [Fact]
    public void FrameworkRpcStreamHandle_RemainsRejectedAsTransportControlType()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using DotBoxD.Services.Protocol;
            using System.Threading.Tasks;

            namespace Regress.RpcStreamHandleIdentity
            {
                [RpcService]
                public interface IFrameworkStreamHandleService
                {
                    Task<int> SendAsync(RpcStreamHandle request);
                }
            }
            """;

        var runResult = Compile(source);

        runResult.Diagnostics.Should().ContainSingle(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("RpcStreamHandle"));
    }

    private static GeneratorDriverRunResult Compile(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var memoryStream = new MemoryStream();
        var emit = finalCompilation.Emit(memoryStream);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));

        return runResult;
    }
}
