using System.IO.Pipelines;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.SubServices;

public class RequiredMemberSubServiceCompanionTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Fact]
    public void MetadataOnlyCompanionWithUnsetRequiredMember_BecomesUnsupportedStub()
    {
        var runResult = RunCompanion(hasSetsRequiredMembers: false, out var compilation);

        AssertUnsupportedCompanion(runResult, compilation);
    }

    [Fact]
    public void MetadataOnlyCompanionWithSetsRequiredMembers_RemainsAvailable()
    {
        var runResult = RunCompanion(hasSetsRequiredMembers: true, out var compilation);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS002");

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("new global::ReferencedContracts.SubProxy(this._invoker, __dotboxd_handle.InstanceId)");

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().Contain("case \"GetSubAsync\":");

        AssertCompiles(compilation, runResult);
    }

    private static GeneratorDriverRunResult RunCompanion(
        bool hasSetsRequiredMembers,
        out CSharpCompilation compilation)
    {
        var setsRequiredMembers = hasSetsRequiredMembers
            ? "[SetsRequiredMembers]"
            : string.Empty;
        var referenced = CompileReference($$"""
            using DotBoxD.Services.Attributes;
            using System.Diagnostics.CodeAnalysis;
            using System.Threading.Tasks;

            namespace ReferencedContracts;

            [RpcService]
            public interface ISub
            {
                Task<int> CountAsync();
            }

            public sealed class SubProxy : ISub
            {
                {{setsRequiredMembers}}
                public SubProxy(global::DotBoxD.Services.Server.IRpcInvoker invoker, string instanceId)
                {
                }

                public required string Name { get; init; }

                public Task<int> CountAsync() => Task.FromResult(0);
            }
            """);
        compilation = CSharpCompilation.Create(
            assemblyName: "RequiredMemberSubService_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: [CSharpSyntaxTree.ParseText("""
                using DotBoxD.Services.Attributes;
                using System.Threading.Tasks;
                using ReferencedContracts;

                namespace Consumer;

                [RpcService]
                public interface IRoot
                {
                    Task<ISub> GetSubAsync();
                }
                """, s_parseOptions)],
            references: CreateBaseReferences().Append(referenced),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();
    }

    private static void AssertUnsupportedCompanion(
        GeneratorDriverRunResult runResult,
        CSharpCompilation compilation)
    {
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("global::ReferencedContracts.ISub") &&
            d.GetMessage().Contains("cannot be proxied because that service was not generated"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("throw new global::System.NotSupportedException");
        proxy.Should().NotContain("new global::ReferencedContracts.SubProxy");

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"GetSubAsync\":");

        AssertCompiles(compilation, runResult);
    }

    private static MetadataReference CompileReference(string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "ReferencedContracts_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, s_parseOptions)],
            references: CreateBaseReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        emit.Success.Should().BeTrue(FormatErrors(emit.Diagnostics));
        return MetadataReference.CreateFromImage(ms.ToArray());
    }

    private static void AssertCompiles(CSharpCompilation compilation, GeneratorDriverRunResult runResult)
    {
        using var ms = new MemoryStream();
        var emit = compilation.AddSyntaxTrees(runResult.GeneratedTrees).Emit(ms);
        emit.Success.Should().BeTrue(FormatErrors(emit.Diagnostics));
    }

    private static string FormatErrors(IEnumerable<Diagnostic> diagnostics)
        => string.Join(
            "\n",
            diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));

    private static IEnumerable<MetadataReference> CreateBaseReferences()
    {
        foreach (var reference in Basic.Reference.Assemblies.Net80.References.All)
        {
            yield return reference;
        }

        yield return MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(Pipe).Assembly.Location);
    }
}
