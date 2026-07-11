using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class ParameterlessStreamingReturnRegressionTests
{
    [Fact]
    public void ParameterlessStreamingReturns_UseNoRequestStreamingOverloads()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Collections.Generic;
            using System.IO;
            using System.IO.Pipelines;
            using System.Threading.Tasks;

            namespace Regress.ParameterlessStreamingReturns
            {
                [RpcService]
                public interface IDownloads
                {
                    IAsyncEnumerable<int> Numbers();
                    Task<Stream> DownloadAsync();
                    ValueTask<Pipe> PipeAsync();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.ParameterlessStreamingReturns",
                "IDownloads",
                GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();

        proxy.Should().Contain("InvokeAsyncEnumerable<int>(\"IDownloads\", \"Numbers\"");
        proxy.Should().Contain("InvokeStreamAsync(\"IDownloads\", \"DownloadAsync\"");
        proxy.Should().Contain("InvokePipeAsync(\"IDownloads\", \"PipeAsync\"");
        proxy.Should().NotContain("RpcStreamAttachment[]?)null");
    }
}
