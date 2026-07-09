using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PluginServer = DotBoxD.Plugins.PluginServer;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

internal static class HookChainGeneratorTestSupport
{
    internal static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    internal static GeneratorDriverRunResult RunGenerator(string source)
        => RunGeneratorCore(source).Result;

    internal static GeneratorDriverRunResult RunGenerator(params SyntaxTree[] syntaxTrees)
        => RunGeneratorCore(syntaxTrees).Result;

    internal static Compilation RunGeneratorCompilation(string source)
        => RunGeneratorCore(source).Output;

    internal static string RemotePluginServerUsageSource(string configureBody)
        => RemotePluginServerSource($$"""
            public sealed record DamageEvent(string TargetId);
            public sealed class DamageKernel;

            public sealed class Usage
            {
                public RemotePluginServer Server { get; init; } = null!;

                public void Configure()
                {
                    {{configureBody}}
                }
            }
            """);

    internal static string RemotePluginServerSource(string pluginMembers)
        => $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Services.Attributes;

            namespace Sample.Game
            {
                [RpcService]
                public interface IGameWorld;
            }

            namespace Sample.Game.Ipc
            {
                public readonly record struct LiveSettingUpdate(string Name, string Value);

                public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
                {
                    ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask UpdateSettingsAsync(
                        string pluginId,
                        LiveSettingUpdate[] updates,
                        bool atomic = false,
                        CancellationToken ct = default);
                    ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
                }
            }

            namespace Sample.Plugin
            {
                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : Sample.Game.IGameWorld;

                public sealed partial class RemotePluginContext;

            {{pluginMembers}}
            }
            """;

    internal static (Compilation Output, GeneratorDriverRunResult Result) RunGeneratorCore(string source)
        => RunGeneratorCore([CSharpSyntaxTree.ParseText(source, ParseOptions)]);

    private static (Compilation Output, GeneratorDriverRunResult Result) RunGeneratorCore(SyntaxTree[] syntaxTrees)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDHookChainGeneratorTest",
            syntaxTrees,
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginServer).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(
                    typeof(DotBoxD.Services.Attributes.RpcServiceAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (output, PluginGeneratorAssert.NoUnexpectedSourceGeneratorFailures(driver.GetRunResult()));
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
