using System.Collections.Immutable;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Services.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncForeignRpcServiceAttributeIdentitySurpriseTests
{
    [Fact]
    public void Generated_facade_ignores_foreign_RpcService_world_when_lowering_InvokeAsync()
    {
        var result = RunGenerator(Source, CompileForeignRpcServiceAttribute());
        var generatedSource = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("AnonymousInvokeAsync", generatedSource, StringComparison.Ordinal);
    }

    private static GeneratorDriverRunResult RunGenerator(string source, MetadataReference foreignAttribute)
    {
        var compilation = CSharpCompilation.Create(
            "InvokeAsyncForeignRpcServiceAttributeIdentity",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            References().Append(foreignAttribute),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return PluginGeneratorAssert.NoUnexpectedSourceGeneratorFailures(driver.GetRunResult());
    }

    private static MetadataReference CompileForeignRpcServiceAttribute()
    {
        var compilation = CSharpCompilation.Create(
            "ForeignRpcServiceAttribute",
            [CSharpSyntaxTree.ParseText(ForeignAttributeSource, ParseOptions)],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));

        return MetadataReference.CreateFromImage(stream.ToArray())
            .WithAliases(ImmutableArray.Create("Foreign"));
    }

    private static IEnumerable<MetadataReference> References()
        => TrustedPlatformReferences()
            .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location));

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    private const string ForeignAttributeSource = """
        namespace DotBoxD.Services.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Interface)]
            public sealed class RpcServiceAttribute : System.Attribute;
        }
        """;

    private const string Source = """
        extern alias Foreign;

        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [Foreign::DotBoxD.Services.Attributes.RpcService]
        public interface IForeignWorld
        {
        }

        [RpcService]
        public interface IRealWorld
        {
            [HostBinding("host.world.read", "world.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int Read();
        }

        [GeneratePluginServer]
        public partial class PluginServer : IForeignWorld, IRealWorld;

        public static class Usage
        {
            public static ValueTask<int> Run(PluginServer server)
                => server.InvokeAsync(async (IRealWorld world) => world.Read());
        }
        """;
}
