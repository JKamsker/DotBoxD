using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerPlatformCompatibilityAttributeSurpriseTests
{
    [Fact]
    public void Generated_plugin_server_preserves_platform_compatibility_attributes_on_forwarded_members()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var normalized = generated.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Runtime.Versioning.SupportedOSPlatformAttribute(\"windows\")]\n" +
            "    public global::System.Threading.Tasks.ValueTask<int> WindowsPingAsync(",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Runtime.Versioning.SupportedOSPlatformAttribute(\"windows\")]\n" +
            "        public global::System.Threading.Tasks.ValueTask<int> WindowsRenameAsync(",
            normalized,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_keeps_unannotated_forwarded_members_clean()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var normalized = generated.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SupportedOSPlatformAttribute(\"windows\")]\n" +
            "    public global::System.Threading.Tasks.ValueTask<int> CrossPlatformPingAsync(",
            normalized,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_preserves_platform_guard_attributes_on_synchronous_facade_and_wrapper_members()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var normalized = generated.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Runtime.Versioning.SupportedOSPlatformGuardAttribute(\"windows\")]\n" +
            "    public bool IsWindows()",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Runtime.Versioning.UnsupportedOSPlatformGuardAttribute(\"windows\")]\n" +
            "    public bool IsNotWindows()",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Runtime.Versioning.SupportedOSPlatformGuardAttribute(\"windows\")]\n" +
            "        public bool IsWindows()",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Runtime.Versioning.UnsupportedOSPlatformGuardAttribute(\"windows\")]\n" +
            "        public bool IsNotWindows()",
            normalized,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SupportedOSPlatformGuard")]
    [InlineData("UnsupportedOSPlatformGuard")]
    public void Aliased_foreign_guards_do_not_become_framework_guards_on_facades_and_wrappers(string name)
    {
        var references = (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ?? [])
            .Select(path => MetadataReference.CreateFromFile(path));
        var foreign = CSharpCompilation.Create("ForeignGuard", [CSharpSyntaxTree.ParseText($$"""
            namespace System.Runtime.Versioning;
            public sealed class {{name}}Attribute(string platform) : System.Attribute;
            """)], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = foreign.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        var reference = MetadataReference.CreateFromImage(stream.ToArray()).WithAliases(ImmutableArray.Create("ForeignGuard"));
        var source = "extern alias ForeignGuard;\n" + ServerSource.Replace("[" + name + "(",
            "[ForeignGuard::System.Runtime.Versioning." + name + "(", StringComparison.Ordinal);
        var (_, output, diagnostics) = PluginServerGenerationTestDriver.RunWithDiagnostics(source, reference);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(output);
        var server = output.GetTypeByMetadataName("Regression.Plugin.RemotePluginServer")!;
        var methods = new[] { server }.Concat(server.GetTypeMembers()).SelectMany(type => type.GetMembers()).OfType<IMethodSymbol>()
            .Where(method => method.Name == (name == "SupportedOSPlatformGuard" ? "IsWindows" : "IsNotWindows")).ToArray();
        Assert.Equal(2, methods.Length);
        Assert.All(methods, method => Assert.DoesNotContain(method.GetAttributes(), attribute =>
            attribute.AttributeClass?.Name == name + "Attribute"));
    }

    private const string ServerSource = """
        #nullable enable
        using System.Runtime.Versioning;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Regression.Game
        {
            [RpcService]
            public interface IGameWorldAccess
            {
                [SupportedOSPlatform("windows")]
                ValueTask<int> WindowsPingAsync();

                [SupportedOSPlatformGuard("windows")]
                bool IsWindows();

                [UnsupportedOSPlatformGuard("windows")]
                bool IsNotWindows();

                ValueTask<int> CrossPlatformPingAsync();

                IMonsterControl Monsters { get; }
            }

            [RpcService]
            public interface IMonsterControl
            {
                [SupportedOSPlatform("windows")]
                ValueTask<int> WindowsRenameAsync(string value);

                [SupportedOSPlatformGuard("windows")]
                bool IsWindows();

                [UnsupportedOSPlatformGuard("windows")]
                bool IsNotWindows();
            }
        }

        namespace Regression.Game.Ipc
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

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static Regression.Game.IGameWorldAccess GetGameWorldAccess(
                    DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new System.InvalidOperationException("not used");
            }
        }

        namespace Regression.Plugin
        {
            using DotBoxD.Abstractions;
            using Regression.Game;

            [GeneratePluginServer(Context = typeof(RemotePluginContext))]
            public partial class RemotePluginServer : IGameWorldAccess;

            public sealed partial class RemotePluginContext;
        }
        """;
}
