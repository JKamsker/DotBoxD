using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Services.SourceGenerator.EntryPoint;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerGeneratedAssemblyCollisionTests
{
    private static readonly CSharpParseOptions s_parseOptions = CSharpParseOptions.Default
        .WithLanguageVersion(LanguageVersion.Preview);

    private const string Contracts = """
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Services.Attributes;
        namespace Collision.Game
        {
            [RpcService]
            public interface IGameWorldAccess;
        }
        namespace Collision.Game.Ipc
        {
            public readonly record struct LiveSettingUpdate(string Name, string Value);
            public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
            {
                ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                ValueTask UpdateSettingsAsync(string pluginId, LiveSettingUpdate[] updates,
                    bool atomic = false, CancellationToken ct = default);
                ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
            }
            [RpcService]
            public interface IPluginEventCallback
            {
                ValueTask OnEventAsync(string subscriptionId, System.ReadOnlyMemory<byte> value, CancellationToken ct = default);
                ValueTask<byte[]> OnResultAsync(string subscriptionId, System.ReadOnlyMemory<byte> value, CancellationToken ct = default);
            }
        }
        """;

    private const string Facade = """
        namespace Collision.Plugin
        {
            [DotBoxD.Abstractions.GeneratePluginServer(Context = typeof(PluginContext))]
            public partial class PluginServer : Collision.Game.IGameWorldAccess;
            public sealed partial class PluginContext;
            [DotBoxD.Services.Attributes.RpcService]
            public interface ILocalService { int Read(); }
        }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Facade_uses_contract_assembly_extensions_without_type_conflicts(bool importedWorld)
    {
        var b = RunGenerators(Create("ContractsB", importedWorld ? Contracts : """
            namespace Other
            {
                [DotBoxD.Services.Attributes.RpcService]
                public interface IOtherService { int Read(); }
            }
            """), includePluginGenerator: false);
        using var stream = new MemoryStream();
        var emit = b.Emit(stream);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        var a = Create("ContractsA", importedWorld ? Facade : Contracts + Facade)
            .AddReferences(MetadataReference.CreateFromImage(stream.ToArray()));
        var output = RunGenerators(a, includePluginGenerator: true);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error ||
            d.Id is "CS0436" or "CS0433"));
        var generated = string.Join("\n", output.SyntaxTrees.Select(tree => tree.ToString()));
        Assert.Contains("DotBoxDGeneratedExtensions_Contracts" + (importedWorld ? "B" : "A") +
            ".ProvidePluginEventCallback(peer, new RemoteLocalEventSink(_localHandlers))", generated, StringComparison.Ordinal);
        Assert.Contains("DotBoxDGeneratedExtensions_Contracts" + (importedWorld ? "B" : "A") +
            ".GetGameWorldAccess(session.Peer)", generated, StringComparison.Ordinal);
    }

    private static Compilation RunGenerators(CSharpCompilation input, bool includePluginGenerator)
    {
        var generators = new List<ISourceGenerator> { new DotBoxDRpcGenerator().AsSourceGenerator() };
        if (includePluginGenerator)
        {
            generators.Add(new PluginPackageGenerator().AsSourceGenerator());
        }

        CSharpGeneratorDriver.Create(generators, parseOptions: s_parseOptions)
            .RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        return output;
    }

    private static CSharpCompilation Create(string assemblyName, string source)
    {
        var references = (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(path => MetadataReference.CreateFromFile(path));
        return CSharpCompilation.Create(assemblyName,
            [CSharpSyntaxTree.ParseText(source, s_parseOptions)],
            references
                .Append(MetadataReference.CreateFromFile(typeof(GeneratePluginServerAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Peer.RpcPeer).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
