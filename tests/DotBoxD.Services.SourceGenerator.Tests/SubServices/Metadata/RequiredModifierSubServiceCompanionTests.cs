using System.IO.Pipelines;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;

namespace DotBoxD.Services.SourceGenerator.Tests.SubServices;

public sealed class RequiredModifierSubServiceCompanionTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Fact]
    public void MetadataOnlyCompanionWithRequiredModifierConstructor_BecomesUnsupportedStub()
    {
        var referenced = CompileReference("""
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace ReferencedContracts;

            public sealed class RequiredModifier
            {
            }

            [RpcService]
            public interface ISub
            {
                Task<int> CountAsync();
            }

            public sealed class SubProxy : ISub
            {
                public SubProxy(global::DotBoxD.Services.Server.IRpcInvoker invoker, string instanceId)
                {
                }

                public Task<int> CountAsync() => Task.FromResult(0);
            }
            """);
        var compilation = CreateCompilation("""
            using DotBoxD.Services.Attributes;
            using ReferencedContracts;
            using System.Threading.Tasks;

            namespace Consumer;

            [RpcService]
            public interface IRoot
            {
                Task<ISub> GetSubAsync();
            }
            """, referenced);

        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        runResult.Diagnostics.Should().ContainSingle(d => d.Id == "DBXS002" &&
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

        using var assembly = new MemoryStream();
        var emit = compilation.Emit(assembly);
        emit.Success.Should().BeTrue(FormatErrors(emit.Diagnostics));
        return MetadataReference.CreateFromImage(AddRequiredModifier(assembly.ToArray()));
    }

    private static byte[] AddRequiredModifier(byte[] assemblyImage)
    {
        using var input = new MemoryStream(assemblyImage);
        using var definition = AssemblyDefinition.ReadAssembly(input);
        var module = definition.MainModule;
        var proxy = module.GetType("ReferencedContracts.SubProxy");
        var constructor = proxy.Methods.Single(method => method.IsConstructor && method.Parameters.Count == 2);
        var invoker = constructor.Parameters[0];
        var modifier = module.GetType("ReferencedContracts.RequiredModifier");
        invoker.ParameterType = new RequiredModifierType(modifier, invoker.ParameterType);

        using var output = new MemoryStream();
        definition.Write(output);
        return output.ToArray();
    }

    private static CSharpCompilation CreateCompilation(string source, MetadataReference referenced)
        => CSharpCompilation.Create(
            assemblyName: "RequiredModifierSubService_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, s_parseOptions)],
            references: CreateBaseReferences().Append(referenced),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static void AssertCompiles(CSharpCompilation compilation, GeneratorDriverRunResult runResult)
    {
        using var assembly = new MemoryStream();
        var emit = compilation.AddSyntaxTrees(runResult.GeneratedTrees).Emit(assembly);
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
