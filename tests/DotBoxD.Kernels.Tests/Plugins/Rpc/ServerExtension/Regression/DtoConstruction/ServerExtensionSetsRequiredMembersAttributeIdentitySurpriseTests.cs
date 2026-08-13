using System.Collections.Immutable;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionSetsRequiredMembersAttributeIdentitySurpriseTests
{
    [Fact]
    public void Server_extension_rejects_foreign_SetsRequiredMembers_attribute_on_required_read_only_dto()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.DiagnosticsWithReferences(
            ForeignAttributeDtoSource,
            CompileForeignSetsRequiredMembersReference());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DBXK100" &&
            diagnostic.GetMessage().Contains("required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Server_extension_accepts_real_SetsRequiredMembers_attribute_on_required_read_only_dto()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(RealAttributeDtoSource);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK100");
    }

    private static MetadataReference CompileForeignSetsRequiredMembersReference()
    {
        var compilation = CSharpCompilation.Create(
            "ForeignSetsRequiredMembers",
            [CSharpSyntaxTree.ParseText(ForeignAttributeSource)],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));

        return MetadataReference.CreateFromImage(stream.ToArray())
            .WithAliases(ImmutableArray.Create("Foreign"));
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(reference => MetadataReference.CreateFromFile(reference));

    private const string ForeignAttributeSource = """
        namespace System.Diagnostics.CodeAnalysis;

        [System.AttributeUsage(System.AttributeTargets.Constructor)]
        public sealed class SetsRequiredMembersAttribute : System.Attribute
        {
        }
        """;

    private const string ForeignAttributeDtoSource = """
        extern alias Foreign;

        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IRemoteWorldControl
        {
        }

        public sealed class RequiredProfile
        {
            [Foreign::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
            public RequiredProfile(int health) => Health = health;

            public required int Health { get; }
        }

        public interface IWorld
        {
            [HostBinding("host.profile.read", "profile.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            RequiredProfile ReadProfile(int id);
        }

        [ServerExtension(typeof(IRemoteWorldControl), "required-profile")]
        public sealed partial class RequiredProfileKernel
        {
            [ServerExtensionMethod(typeof(IRemoteWorldControl))]
            public RequiredProfile Read(int id, HookContext ctx) => ctx.Host<IWorld>().ReadProfile(id);
        }
        """;

    private const string RealAttributeDtoSource = """
        using System.Diagnostics.CodeAnalysis;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IRemoteWorldControl
        {
        }

        public sealed class RequiredProfile
        {
            [SetsRequiredMembers]
            public RequiredProfile(int health) => Health = health;

            public required int Health { get; }
        }

        public interface IWorld
        {
            [HostBinding("host.profile.read", "profile.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            RequiredProfile ReadProfile(int id);
        }

        [ServerExtension(typeof(IRemoteWorldControl), "required-profile")]
        public sealed partial class RequiredProfileKernel
        {
            [ServerExtensionMethod(typeof(IRemoteWorldControl))]
            public RequiredProfile Read(int id, HookContext ctx) => ctx.Host<IWorld>().ReadProfile(id);
        }
        """;
}
