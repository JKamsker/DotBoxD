using DotBoxD.Services.Attributes;
using DotBoxD.Services.SourceGenerator.EntryPoint;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Generation.Services;

public sealed class ExperimentalSubServiceCompanionCoverageTests
{
    [Theory]
    [InlineData("[Experimental(\"DBXEXP_COMPANION\")]", "")]
    [InlineData("", "[Experimental(\"DBXEXP_COMPANION\")]")]
    public void Experimental_manual_companion_is_not_selected(
        string typeAttribute,
        string constructorAttribute)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "ExperimentalCompanion",
            syntaxTrees: [CSharpSyntaxTree.ParseText($$"""
                using DotBoxD.Services.Attributes;
                using System.Diagnostics.CodeAnalysis;
                using System.Threading.Tasks;

                namespace Contracts;

                [RpcService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                {{typeAttribute}}
                public sealed class SubProxy : ISub
                {
                    {{constructorAttribute}}
                    public SubProxy(IRpcInvoker invoker, string instanceId) { }

                    public Task<int> CountAsync() => Task.FromResult(0);
                }

                [RpcService]
                public interface IRoot
                {
                    Task<ISub> GetSubAsync();
                }
                """)],
            references: TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location)),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new DotBoxDRpcGenerator().AsSourceGenerator());
        var result = driver.RunGenerators(compilation).GetRunResult();

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DBXS002");
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences() =>
        (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
        .Select(static path => MetadataReference.CreateFromFile(path));
}
