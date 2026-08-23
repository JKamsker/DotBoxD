using System.Collections.Immutable;
using DotBoxD.Services.Attributes;
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

    [Fact]
    public void FrameworkRpcStreamHandle_UsesRpcServiceAttributeAssemblyWhenAliasedLookalikePrecedesIt()
    {
        var foreignServices = CompileForeignServicesReference();
        var source = """
            extern alias Foreign;

            using DotBoxD.Services.Attributes;
            using DotBoxD.Services.Protocol;
            using System.Threading.Tasks;

            namespace Regress.AliasedRpcStreamHandleIdentity
            {
                [RpcService]
                public interface IFrameworkStreamHandleService
                {
                    Task<int> SendAsync(RpcStreamHandle request);
                }
            }
            """;
        var compilation = CSharpCompilation.Create(
            assemblyName: "AliasedRpcStreamHandleIdentity",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
            references: Basic.Reference.Assemblies.Net80.References.All
                .Append(foreignServices)
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location)),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        runResult.Diagnostics.Should().ContainSingle(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("RpcStreamHandle", StringComparison.Ordinal));
    }

    private static MetadataReference CompileForeignServicesReference()
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "DotBoxD.Services",
            syntaxTrees: new[]
            {
                CSharpSyntaxTree.ParseText("namespace DotBoxD.Services.Protocol; public sealed class RpcStreamHandle;")
            },
            references: Basic.Reference.Assemblies.Net80.References.All,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join("\n", emit.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray())
            .WithAliases(ImmutableArray.Create("Foreign"));
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
